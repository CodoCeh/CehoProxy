using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class DoctorProgressTests
{
    [Fact]
    public async Task Subscription_progress_stays_before_following_diagnostic_stage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cfg = new CehoConfig
            {
                Subscriptions = { new SubscriptionEntry { Name = "test", Url = "https://example.invalid/sub" } }
            };
            var report = new RecordingReport();
            var tools = new DoctorTools
            {
                Pool = p =>
                {
                    p.Stage("download", 0);
                    p.Stage("complete", 100);
                    return Task.FromResult<IReadOnlyList<ProxyNode>>(Array.Empty<ProxyNode>());
                }
            };
            await Doctor.CheckAsync(cfg, root, tools, report);
            Assert.Contains(74, report.Percentages);
            Assert.Equal(report.Percentages.OrderBy(x => x), report.Percentages);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class RecordingReport : IStageReport
    {
        public List<int> Percentages { get; } = new();
        public void Stage(string text, int percent) => Percentages.Add(percent);
        public void Note(string text) { }
    }
}
