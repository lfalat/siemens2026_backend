using Kernel.Errors;
using Kernel.Exceptions;

namespace Tests.Shared.Exceptions;

/// <summary>
/// Tests the Shared/Kernel <see cref="ValidationErrors"/> collector: it accumulates field-keyed messages
/// and their stable codes, only throws when something was added, and surfaces every key, message and
/// index-aligned code on the <see cref="ValidationException"/>.
/// </summary>
public class ValidationErrorsTests
{
    private static FieldErrorCode Code(string value) => new(value, value);

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

        errors.Add("field", Code("some.code"), "bad");

        Assert.True(errors.HasErrors);
    }

    [Fact]
    public void Throws_with_all_keys_and_codes()
    {
        ValidationErrors errors = new();
        errors.Add("name", Code("item.name_required"), "required");
        errors.Add("serial", Code("item.serial_too_long"), "too long");

        ValidationException ex = Assert.Throws<ValidationException>(errors.ThrowIfAny);

        Assert.Contains("name", ex.Errors.Keys);
        Assert.Contains("serial", ex.Errors.Keys);
        // The code map carries the same keys as the message map.
        Assert.Equal(["item.name_required"], ex.ErrorCodeValues["name"]);
        Assert.Equal(["item.serial_too_long"], ex.ErrorCodeValues["serial"]);
    }

    [Fact]
    public void Collects_multiple_messages_and_codes_per_key_in_order()
    {
        ValidationErrors errors = new();
        errors.Add("field", Code("field.first"), "first");
        errors.Add("field", Code("field.second"), "second");

        ValidationException ex = Assert.Throws<ValidationException>(errors.ThrowIfAny);

        // Messages and codes stay index-aligned, in insertion order.
        Assert.Equal(["first", "second"], ex.Errors["field"]);
        Assert.Equal(["field.first", "field.second"], ex.ErrorCodeValues["field"]);
    }
}
