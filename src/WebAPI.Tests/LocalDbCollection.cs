using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Every test class that reads or writes the shared local SQL Server joins this
/// collection, so xUnit runs them one after another instead of in parallel. They share
/// real rows (e.g. the dev DB's only package, whose slot count several of them change
/// and assert on), and parallel runs made those assertions flaky.
/// </summary>
[CollectionDefinition(Name)]
public class LocalDbCollection
{
    public const string Name = "LocalDb";
}
