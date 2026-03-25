using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class FamilyXrayService
{
    internal FamilyXrayResponse Xray(PlatformServices services, Document doc, FamilyXrayRequest request)
    {
        request ??= new FamilyXrayRequest();

        var response = new FamilyXrayResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            SourceDocumentTitle = doc.Title ?? string.Empty,
            IsFamilyDocument = doc.IsFamilyDocument
        };

        var family = ResolveFamily(doc, request);
        response.FamilyId = checked((int)family.Id.Value);
        response.FamilyName = family.Name ?? string.Empty;
        response.CategoryName = family.FamilyCategory?.Name ?? string.Empty;
        response.TypesCount = family.GetFamilySymbolIds().Count;
        response.TypeNames = family.GetFamilySymbolIds()
            .Select(x => doc.GetElement(x) as FamilySymbol)
            .Where(x => x != null)
            .Select(x => x!.Name ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxTypeNames))
            .ToList();

        if (family.IsInPlace)
        {
            response.Issues.Add("Family is in-place; nested and FamilyManager details are limited.");
        }

        Document? familyDoc = null;
        var closeFamilyDoc = false;
        try
        {
            if (doc.IsFamilyDocument)
            {
                familyDoc = doc;
            }
            else if (!family.IsInPlace && family.IsEditable)
            {
                familyDoc = doc.EditFamily(family);
                closeFamilyDoc = familyDoc != null;
            }
            else if (!family.IsEditable)
            {
                response.Issues.Add("Family is not editable; deep family document analysis was skipped.");
            }

            if (familyDoc != null)
            {
                response.TemplateHint = familyDoc.Title ?? string.Empty;
                PopulateFamilyDocumentDetails(response, familyDoc, request);
            }
            else if (string.IsNullOrWhiteSpace(response.TemplateHint))
            {
                response.TemplateHint = response.CategoryName;
            }
        }
        catch (Exception ex)
        {
            response.Issues.Add("Failed to inspect family document: " + ex.Message);
        }
        finally
        {
            if (closeFamilyDoc && familyDoc != null)
            {
                try
                {
                    familyDoc.Close(false);
                }
                catch
                {
                    response.Issues.Add("Opened family document could not be closed automatically.");
                }
            }
        }

        if (!response.IncludeAnyDeepData())
        {
            response.Issues.Add("Deep family anatomy is empty; response is limited to project-level family info.");
        }

        response.Summary = string.Format(
            CultureInfo.InvariantCulture,
            "Family `{0}` has {1} type(s), {2} nested family group(s), {3} connector(s), and {4} issue(s).",
            response.FamilyName,
            response.TypesCount,
            response.NestedFamilies.Count,
            response.Connectors.Count,
            response.Issues.Count);

        return response;
    }

    private static Family ResolveFamily(Document doc, FamilyXrayRequest request)
    {
        if (doc.IsFamilyDocument)
        {
            var ownerFamily = doc.OwnerFamily ?? throw new InvalidOperationException("Current family document has no owner family.");
            if (request.FamilyId > 0 && checked((int)ownerFamily.Id.Value) != request.FamilyId)
            {
                throw new InvalidOperationException("Requested FamilyId does not match the active family document.");
            }

            if (!string.IsNullOrWhiteSpace(request.FamilyName)
                && !string.Equals(ownerFamily.Name, request.FamilyName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Requested FamilyName does not match the active family document.");
            }

            return ownerFamily;
        }

        if (request.FamilyId > 0)
        {
            var element = doc.GetElement(new ElementId((long)request.FamilyId));
            if (element is Family directFamily)
            {
                return directFamily;
            }

            if (element is FamilySymbol symbol && symbol.Family != null)
            {
                return symbol.Family;
            }

            if (element is FamilyInstance instance && instance.Symbol?.Family != null)
            {
                return instance.Symbol.Family;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.FamilyName))
        {
            var byName = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(x => string.Equals(x.Name, request.FamilyName, StringComparison.OrdinalIgnoreCase));

            if (byName != null)
            {
                return byName;
            }
        }

        throw new InvalidOperationException("Family not found for xray.");
    }

    private static void PopulateFamilyDocumentDetails(FamilyXrayResponse response, Document familyDoc, FamilyXrayRequest request)
    {
        response.SourceDocumentTitle = familyDoc.Title ?? response.SourceDocumentTitle;

        var familyManager = familyDoc.FamilyManager;
        if (familyManager != null)
        {
            var parameters = new List<FamilyParameter>();
            foreach (FamilyParameter parameter in familyManager.Parameters)
            {
                parameters.Add(parameter);
            }

            foreach (var parameter in parameters
                         .OrderBy(x => x.Definition?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .Take(Math.Max(1, request.MaxParameters)))
            {
                var name = parameter.Definition?.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (parameter.IsInstance)
                {
                    response.InstanceParameters.Add(name);
                }
                else
                {
                    response.TypeParameters.Add(name);
                }

                var formula = parameter.Formula ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(formula))
                {
                    response.FormulaParameters.Add(new FamilyFormulaInfo
                    {
                        ParameterName = name,
                        Formula = formula
                    });
                }
            }
        }

        if (request.IncludeNestedFamilies)
        {
            var nestedGroups = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(x => x.Symbol?.Family != null)
                .GroupBy(
                    x => checked((int)x.Symbol!.Family.Id.Value),
                    x => x,
                    (familyId, items) =>
                    {
                        var first = items.First();
                        var nestedFamily = first.Symbol!.Family;
                        return new FamilyNestedFamilyInfo
                        {
                            FamilyName = nestedFamily.Name ?? string.Empty,
                            CategoryName = nestedFamily.FamilyCategory?.Name ?? string.Empty,
                            IsShared = nestedFamily.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.AsInteger() == 1,
                            Count = items.Count()
                        };
                    })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.FamilyName, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, request.MaxNestedFamilies))
                .ToList();

            response.NestedFamilies.AddRange(nestedGroups);
        }

        if (request.IncludeReferencePlanes)
        {
            response.ReferencePlanes = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Select(x => x.Name ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (request.IncludeConnectors)
        {
            response.Connectors = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(ConnectorElement))
                .Cast<ConnectorElement>()
                .OrderBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(x => new FamilyConnectorInfo
                {
                    ConnectorId = checked((int)x.Id.Value),
                    Name = x.Name ?? string.Empty,
                    Domain = x.Domain.ToString(),
                    SystemClassification = x.SystemClassification.ToString(),
                    Shape = x.Shape.ToString(),
                    DirectionSummary = FormatDirection(x.Direction)
                })
                .ToList();
        }

        if (response.Connectors.Count == 0 && LooksLikeMepFamily(response.CategoryName))
        {
            response.Issues.Add("No connectors were found even though the family looks MEP-related.");
        }

        if (response.ReferencePlanes.Count == 0 && request.IncludeReferencePlanes)
        {
            response.Issues.Add("No named reference planes were found.");
        }
    }

    private static string FormatDirection(XYZ direction)
    {
        if (direction == null)
        {
            return string.Empty;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "({0:0.###}, {1:0.###}, {2:0.###})",
            direction.X,
            direction.Y,
            direction.Z);
    }

    private static bool LooksLikeMepFamily(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        return categoryName.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0
               || categoryName.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0
               || categoryName.IndexOf("Cable", StringComparison.OrdinalIgnoreCase) >= 0
               || categoryName.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0
               || categoryName.IndexOf("Air", StringComparison.OrdinalIgnoreCase) >= 0
               || categoryName.IndexOf("Mechanical", StringComparison.OrdinalIgnoreCase) >= 0
               || categoryName.IndexOf("Electrical", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class FamilyXrayResponseExtensions
{
    internal static bool IncludeAnyDeepData(this FamilyXrayResponse response)
    {
        return response.NestedFamilies.Count > 0
               || response.InstanceParameters.Count > 0
               || response.TypeParameters.Count > 0
               || response.FormulaParameters.Count > 0
               || response.ReferencePlanes.Count > 0
               || response.Connectors.Count > 0;
    }
}
