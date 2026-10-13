using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using Kernel.Decorators;

namespace Tests.Shared;

/// <summary>
/// Tests the Shared/Kernel <see cref="ValidationDecorator{TCommand,TResult}"/> (and its void variant):
/// it runs DataAnnotations before the inner handler, throws <see cref="Kernel.Exceptions.ValidationException"/>
/// with member-keyed errors on failure, and skips the inner handler when invalid. The inner handler is the
/// shared <see cref="StubHandler{TCommand,TResult}"/>/<see cref="StubVoidHandler{TCommand}"/>.
/// </summary>
public class ValidationDecoratorTests
{
    private sealed record Sample : ICommand
    {
        public static string EventName => "sample_validated";

        [Required, EmailAddress]
        public string Email { get; init; } = string.Empty;

        [Range(1, 5)]
        public int Rating { get; init; }
    }

    [Fact]
    public async Task Valid_command_runs_inner_and_returns_result()
    {
        StubHandler<Sample, string> inner = new(_ => "ok");
        ValidationDecorator<Sample, string> decorator = new(inner);

        string result = await decorator.HandleAsync(
            new Sample { Email = "a@b.com", Rating = 3 }, TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Invalid_command_throws_and_skips_inner()
    {
        StubHandler<Sample, string> inner = new(_ => "ok");
        ValidationDecorator<Sample, string> decorator = new(inner);

        Kernel.Exceptions.ValidationException ex = await Assert.ThrowsAsync<Kernel.Exceptions.ValidationException>(
            () => decorator.HandleAsync(new Sample { Email = "nope", Rating = 9 }, TestContext.Current.CancellationToken));

        Assert.Equal(0, inner.Calls);
        Assert.Contains(nameof(Sample.Email), ex.Errors.Keys);
        Assert.Contains(nameof(Sample.Rating), ex.Errors.Keys);
        // Each failure carries a stable, attribute-derived code the client translates on.
        Assert.Equal(["email"], ex.ErrorCodeValues[nameof(Sample.Email)]);
        Assert.Equal(["range"], ex.ErrorCodeValues[nameof(Sample.Rating)]);
    }

    private sealed record Required : ICommand
    {
        public static string EventName => "required_validated";

        [System.ComponentModel.DataAnnotations.Required]
        public string Name { get; init; } = string.Empty;
    }

    [Fact]
    public async Task A_failing_required_reports_the_required_code_alone()
    {
        StubHandler<Required, string> inner = new(_ => "ok");
        ValidationDecorator<Required, string> decorator = new(inner);

        Kernel.Exceptions.ValidationException ex = await Assert.ThrowsAsync<Kernel.Exceptions.ValidationException>(
            () => decorator.HandleAsync(new Required { Name = null! }, TestContext.Current.CancellationToken));

        Assert.Equal(["required"], ex.ErrorCodeValues[nameof(Required.Name)]);
    }

    [Fact]
    public async Task Void_variant_validates_too()
    {
        StubVoidHandler<Sample> inner = new();
        ValidationDecorator<Sample> decorator = new(inner);

        await Assert.ThrowsAsync<Kernel.Exceptions.ValidationException>(
            () => decorator.HandleAsync(new Sample { Email = "nope", Rating = 0 }, TestContext.Current.CancellationToken));

        Assert.Equal(0, inner.Calls);
    }
}
