using System.Net;
using System.Reflection;
using System.Text;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class JobProgressMarkupTests
{
    private static string Render(JobState state, bool indeterminate, string language)
    {
        var job = new Job { Id = "job-metric", Kind = "test", Title = "Fixture operation" };
        job.Report("Fixture phase", indeterminate ? null : 45, newPhase: true, waiting: indeterminate);
        if (state != JobState.Running) job.Complete("Fixture result", failed: state == JobState.Failed);
        var cfg = new CehoConfig { Language = language };
        var html = new StringBuilder();
        var method = typeof(WebServer).GetMethod("RenderJob", BindingFlags.Static | BindingFlags.NonPublic)!;
        Func<string, object[], string> text = (key, args) => Strings.T(language, key, args);
        method.Invoke(null, new object[] { html, cfg, job, "state", text });
        return WebUtility.HtmlDecode(html.ToString());
    }

    [Theory]
    [InlineData(JobState.Running, "en", "In progress")]
    [InlineData(JobState.Failed, "en", "Failed")]
    [InlineData(JobState.Done, "en", "Done")]
    [InlineData(JobState.Running, "ru", "Выполняется")]
    [InlineData(JobState.Failed, "ru", "Не удалось")]
    [InlineData(JobState.Done, "ru", "Готово")]
    public void Unknown_duration_uses_state_labels_without_claiming_measured_progress(
        JobState state, string language, string expected)
    {
        var html = Render(state, indeterminate: true, language);
        Assert.Contains($"<span class=job-num id=jn>{expected}</span>", html);
        Assert.DoesNotContain("aria-valuenow", html);
        if (state == JobState.Running) Assert.Contains("class=\"bar indeterminate\"", html);
        else Assert.DoesNotContain("class=\"bar indeterminate\"", html);
    }

    [Theory]
    [InlineData(JobState.Running, "45%")]
    [InlineData(JobState.Failed, "45%")]
    [InlineData(JobState.Done, "100%")]
    public void Determinate_duration_keeps_the_existing_percentage(JobState state, string expected)
    {
        var html = Render(state, indeterminate: false, "en");
        Assert.Contains($"<span class=job-num id=jn>{expected}</span>", html);
        Assert.DoesNotContain("class=\"bar indeterminate\"", html);
    }
}
