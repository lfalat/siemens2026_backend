using Kernel.Exceptions;

namespace Tests.Shared.Exceptions;

/// <summary>
/// Tests the Shared/Kernel <see cref="ValidationErrors"/> collector: it accumulates field-keyed messages,
/// only throws when something was added, and surfaces every key and message on the <see cref="ValidationException"/>.
/// </summary>
public class ValidationErrorsTests
{
    [Fact]
    public void Empty_collector_has_no_errors_and_does_not_throw()
    {
        ValidationErrors errors = new();

        Assert.False(errors.HasErrors);
        errors.ThrowIfAny(); // must not throw
    }

    [Fact]
    public void Add_sets_has_errors()
    {
        ValidationErrors errors = new();

        errors.Add("field", "bad");

        Assert.True(errors.HasErrors);
    }

    [Fact]
    public void Throws_with_all_keys()
    {
        ValidationErrors errors = new();
        errors.Add("name", "required");
        errors.Add("serial", "too long");

        ValidationException ex = Assert.Throws<ValidationException>(errors.ThrowIfAny);

        Assert.Contains("name", ex.Errors.Keys);
        Assert.Contains("serial", ex.Errors.Keys);
    }

    [Fact]
    public void Collects_multiple_messages_per_key_in_order()
    {
        ValidationErrors errors = new();
        errors.Add("field", "first");
        errors.Add("field", "second");

        ValidationException ex = Assert.Throws<ValidationException>(errors.ThrowIfAny);

        Assert.Equal(["first", "second"], ex.Errors["field"]);
    }
}
