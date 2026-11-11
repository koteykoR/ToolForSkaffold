using Allure.Net.Commons;
using System.Text.Encodings.Web;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Ak.AutoTest.Reporting;

/// <summary>Allure-шаги с явно выбранными параметрами и вложениями.</summary>
public static class AllureReport
{
    private const int ParameterMaxLength = 4000;

    private static readonly JsonSerializerOptions PrettyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Диагностические данные не участвуют в идентичности теста и его истории.</summary>
    public static void AttachParameters(params (string Name, object? Value)[] parameters) =>
        AttachJson("parameters", JsonSerializer.Serialize(parameters.ToDictionary(p => p.Name, p => p.Value), PrettyJsonOptions));

    public static void Step(string name, Action action, params (string Name, object? Value)[] parameters)
    {
        if (!HasReportingContext())
        {
            action();
            return;
        }

        AllureApi.Step(name, () =>
        {
            AddStepParameters(parameters);
            action();
        });
    }

    public static T Step<T>(string name, Func<T> action, params (string Name, object? Value)[] parameters)
    {
        if (!HasReportingContext())
        {
            return action();
        }

        return AllureApi.Step(name, () =>
        {
            AddStepParameters(parameters);
            return action();
        });
    }

    public static Task StepAsync(string name, Func<Task> action, params (string Name, object? Value)[] parameters)
    {
        if (!HasReportingContext())
        {
            return action();
        }

        return AllureApi.Step(name, async () =>
        {
            AddStepParameters(parameters);
            await action();
        });
    }

    public static Task<T> StepAsync<T>(
        string name,
        Func<Task<T>> action,
        params (string Name, object? Value)[] parameters)
    {
        if (!HasReportingContext())
        {
            return action();
        }

        return AllureApi.Step(name, async () =>
        {
            AddStepParameters(parameters);
            return await action();
        });
    }

    public static void AddStepParameter(string name, object? value)
    {
        if (!AllureLifecycle.Instance.Context.HasStep)
        {
            return;
        }

        AllureLifecycle.Instance.UpdateStep(step =>
            step.parameters.Add(new Parameter
            {
                name = name,
                value = Format(value)
            }));
    }

    public static void RenameStep(string name)
    {
        if (AllureLifecycle.Instance.Context.HasStep)
        {
            AllureApi.SetStepName(name);
        }
    }

    public static void AttachJson(string name, string? json)
    {
        if (!HasReportingContext())
        {
            return;
        }

        var content = string.IsNullOrWhiteSpace(json) ? "null" : PrettyJson(json);
        AllureApi.AddAttachment(name, "application/json", Encoding.UTF8.GetBytes(content), ".json");
    }

    public static void AttachXml(string name, string xml)
    {
        if (!HasReportingContext())
        {
            return;
        }

        AllureApi.AddAttachment(name, "application/xml", Encoding.UTF8.GetBytes(xml), ".xml");
    }

    public static string Format(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is string text)
        {
            return Truncate(text);
        }

        if (value is HttpStatusCode status)
        {
            return $"{(int)status} {status}";
        }

        try
        {
            return Truncate(JsonSerializer.Serialize(value, PrettyJsonOptions));
        }
        catch (NotSupportedException)
        {
            return Truncate(value.ToString() ?? value.GetType().Name);
        }
    }

    private static bool HasReportingContext()
    {
        var context = AllureLifecycle.Instance.Context;
        return context.HasTest || context.HasFixture;
    }

    private static void AddStepParameters((string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            AddStepParameter(name, value);
        }
    }

    private static string PrettyJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document, PrettyJsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= ParameterMaxLength ? value : value[..ParameterMaxLength] + "…";
}
