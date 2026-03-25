using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core.Brain;

public sealed class PersonaRegistry
{
    private readonly Dictionary<string, WorkerPersonaSummary> _personas;

    public PersonaRegistry(string? personasDirectory = null)
    {
        _personas = LoadBuiltIns();
        if (!string.IsNullOrWhiteSpace(personasDirectory))
        {
            LoadFromDirectory(personasDirectory!);
        }
    }

    public WorkerPersonaSummary Resolve(string? personaId)
    {
        var resolvedId = ResolveAlias(personaId);
        return _personas.TryGetValue(resolvedId, out var persona)
            ? Clone(persona)
            : Clone(_personas[WorkerPersonas.RevitWorker]);
    }

    public IReadOnlyList<WorkerPersonaSummary> List()
    {
        return _personas.Values
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(Clone)
            .ToList();
    }

    private void LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var persona = JsonUtil.DeserializeRequired<WorkerPersonaSummary>(File.ReadAllText(file));
                if (string.IsNullOrWhiteSpace(persona.PersonaId))
                {
                    continue;
                }

                _personas[persona.PersonaId.Trim()] = Clone(persona);
            }
            catch
            {
                // fail closed to built-ins
            }
        }
    }

    private static Dictionary<string, WorkerPersonaSummary> LoadBuiltIns()
    {
        return new Dictionary<string, WorkerPersonaSummary>(StringComparer.OrdinalIgnoreCase)
        {
            [WorkerPersonas.RevitWorker] = new WorkerPersonaSummary
            {
                PersonaId = WorkerPersonas.RevitWorker,
                DisplayName = "765T Worker",
                Tone = "pragmatic",
                Expertise = new List<string> { "Revit API", "BIM coordination", "QC", "sheet production" },
                Guardrails = new List<string> { "Luôn plan trước mutation.", "Không delete khi chưa preview.", "Report rõ kết quả và rủi ro." },
                GreetingTemplate = "Chào anh, em là 765T Worker. Em sẽ đọc ngữ cảnh Revit, lập plan ngắn, rồi mới chạy tool."
            },
            [WorkerPersonas.QaReviewer] = new WorkerPersonaSummary
            {
                PersonaId = WorkerPersonas.QaReviewer,
                DisplayName = "QA Reviewer",
                Tone = "strict",
                Expertise = new List<string> { "model health", "standards", "documentation review" },
                Guardrails = new List<string> { "Ưu tiên evidence trước kết luận.", "Không bỏ qua warning có ảnh hưởng downstream.", "Đề xuất verify sau mutate." },
                GreetingTemplate = "Chào anh, em đang ở mode QA Reviewer. Em sẽ soi tiêu chuẩn và residual risk kỹ hơn."
            },
            [WorkerPersonas.Helper] = new WorkerPersonaSummary
            {
                PersonaId = WorkerPersonas.Helper,
                DisplayName = "Helper",
                Tone = "friendly",
                Expertise = new List<string> { "general assistance", "context lookup", "guided next step" },
                Guardrails = new List<string> { "Giải thích ngắn gọn, dễ làm theo.", "Khi thiếu context thì hỏi lại ngắn.", "Không tự ý mutate." },
                GreetingTemplate = "Chào anh, em là Helper. Em sẽ hỗ trợ ngắn gọn và dẫn anh qua từng bước an toàn."
            }
        };
    }

    private static string ResolveAlias(string? personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId))
        {
            return WorkerPersonas.RevitWorker;
        }

        var normalized = (personaId ?? string.Empty).Trim();
        if (string.Equals(normalized, WorkerPersonas.FreelancerDefault, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerPersonas.RevitWorker;
        }

        if (string.Equals(normalized, WorkerPersonas.StrictQaFirm, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerPersonas.QaReviewer;
        }

        if (string.Equals(normalized, WorkerPersonas.ProductionSpeedStudio, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerPersonas.Helper;
        }

        return normalized;
    }

    private static WorkerPersonaSummary Clone(WorkerPersonaSummary persona)
    {
        return new WorkerPersonaSummary
        {
            PersonaId = persona.PersonaId,
            DisplayName = persona.DisplayName,
            Tone = persona.Tone,
            Expertise = persona.Expertise.ToList(),
            Guardrails = persona.Guardrails.ToList(),
            GreetingTemplate = persona.GreetingTemplate
        };
    }
}
