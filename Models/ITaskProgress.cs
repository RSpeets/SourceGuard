namespace SourceGuard.Models;

public interface ITaskProgress
{
    double MaxValue { get; set; }
    double Value    { get; set; }
    string Description { get; set; }
    void Increment(double amount = 1.0);
}
