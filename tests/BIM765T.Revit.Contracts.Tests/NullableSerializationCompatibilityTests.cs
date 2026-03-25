using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class NullableSerializationCompatibilityTests
{
    [Fact]
    public void ElementSummaryDto_Omits_Null_Optional_Fields_And_RoundTrips_When_Present()
    {
        var dto = new ElementSummaryDto
        {
            ElementId = 1001,
            UniqueId = "uid-01",
            DocumentKey = "path:test.rvt",
            CategoryName = "Walls",
            ClassName = "Wall",
            Name = "Basic Wall"
        };

        var json = JsonUtil.Serialize(dto);
        Assert.DoesNotContain("\"LevelId\"", json);
        Assert.DoesNotContain("\"BoundingBox\"", json);
        Assert.DoesNotContain("\"LocationPoint\"", json);

        var roundTrip = JsonUtil.DeserializeRequired<ElementSummaryDto>(json);
        Assert.Null(roundTrip.LevelId);
        Assert.Null(roundTrip.BoundingBox);
        Assert.Null(roundTrip.LocationPoint);

        dto.LevelId = 42;
        dto.BoundingBox = new BoundingBoxDto { MinX = 1, MinY = 2, MinZ = 3, MaxX = 4, MaxY = 5, MaxZ = 6 };
        dto.LocationPoint = new AxisVectorDto { X = 7, Y = 8, Z = 9 };

        var populatedJson = JsonUtil.Serialize(dto);
        Assert.Contains("\"LevelId\":42", populatedJson);
        Assert.Contains("\"BoundingBox\"", populatedJson);
        Assert.Contains("\"LocationPoint\"", populatedJson);

        var populatedRoundTrip = JsonUtil.DeserializeRequired<ElementSummaryDto>(populatedJson);
        Assert.Equal(42, populatedRoundTrip.LevelId);
        Assert.NotNull(populatedRoundTrip.BoundingBox);
        Assert.NotNull(populatedRoundTrip.LocationPoint);
    }

    [Fact]
    public void MutationAndViewDtos_Omit_Null_Optional_Fields()
    {
        var place = new PlaceFamilyInstanceRequest
        {
            DocumentKey = "path:test.rvt",
            FamilySymbolId = 11,
            X = 1,
            Y = 2,
            Z = 3
        };
        var placeJson = JsonUtil.Serialize(place);
        Assert.DoesNotContain("\"StartX\"", placeJson);
        Assert.DoesNotContain("\"EndX\"", placeJson);
        Assert.DoesNotContain("\"FaceNormalX\"", placeJson);

        place.StartX = 4;
        place.EndX = 5;
        place.FaceNormalX = 6;
        var populatedPlaceJson = JsonUtil.Serialize(place);
        Assert.Contains("\"StartX\":4", populatedPlaceJson);
        Assert.Contains("\"EndX\":5", populatedPlaceJson);
        Assert.Contains("\"FaceNormalX\":6", populatedPlaceJson);

        var view = new ViewSummaryDto
        {
            ViewKey = "view:1",
            ViewId = 1,
            Name = "Level 1",
            ViewType = "FloorPlan",
            DocumentKey = "path:test.rvt"
        };
        var viewJson = JsonUtil.Serialize(view);
        Assert.DoesNotContain("\"LevelId\"", viewJson);

        view.LevelId = 99;
        var populatedViewJson = JsonUtil.Serialize(view);
        Assert.Contains("\"LevelId\":99", populatedViewJson);
    }

    [Fact]
    public void ReviewSheetAndObservabilityDtos_Omit_Null_Optional_Fields()
    {
        var review = new ActiveViewSummaryResponse
        {
            DocumentKey = "path:test.rvt",
            ViewKey = "view:1",
            ViewId = 1,
            ViewName = "Active",
            ViewType = "FloorPlan"
        };
        var reviewJson = JsonUtil.Serialize(review);
        Assert.DoesNotContain("\"LevelId\"", reviewJson);

        review.LevelId = 77;
        Assert.Contains("\"LevelId\":77", JsonUtil.Serialize(review));

        var createView = new CreateProjectViewRequest
        {
            DocumentKey = "path:test.rvt",
            ViewKind = "floor_plan"
        };
        var createViewJson = JsonUtil.Serialize(createView);
        Assert.DoesNotContain("\"LevelId\"", createViewJson);

        createView.LevelId = 12;
        Assert.Contains("\"LevelId\":12", JsonUtil.Serialize(createView));

        var context = new TaskContextResponse
        {
            Document = new DocumentSummaryDto { DocumentKey = "path:test.rvt", Title = "Test" },
            ActiveContext = new BIM765T.Revit.Contracts.Context.CurrentContextDto(),
            Selection = new SelectionSummaryDto(),
            Fingerprint = new ContextFingerprint()
        };
        var contextJson = JsonUtil.Serialize(context);
        Assert.DoesNotContain("\"Capabilities\"", contextJson);

        context.Capabilities = new BridgeCapabilities();
        var populatedContextJson = JsonUtil.Serialize(context);
        Assert.Contains("\"Capabilities\"", populatedContextJson);

        var restored = JsonUtil.DeserializeRequired<TaskContextResponse>(populatedContextJson);
        Assert.NotNull(restored.Capabilities);
    }
}
