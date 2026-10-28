using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Storage.Internal;

namespace W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

/// <summary>Checks even already initialized UseModel inputs before EF can compile a query.</summary>
internal sealed class DamengModelRuntimeInitializer(
    ModelRuntimeInitializerDependencies dependencies,
    RelationalModelRuntimeInitializerDependencies relationalDependencies,
    DamengDriver driver) : RelationalModelRuntimeInitializer(dependencies, relationalDependencies)
{
    // EF publishes ModelDependencies before initialization is complete. Both drivers must
    // serialize the entire call, including publication/binding of a separate runtime projection.
    private static readonly object InitializationLock = new();
    private const string DriverAnnotation = "Dameng:RuntimeDriver";

    public override IModel Initialize(
        IModel model,
        bool designTime = true,
        IDiagnosticsLogger<DbLoggerCategory.Model.Validation>? validationLogger = null)
    {
        lock (InitializationLock)
        {
            if (model.ModelDependencies is not null)
            {
                CheckAndBind(model);
            }

            var initialized = base.Initialize(model, designTime, validationLogger);
            // FinalizeModel can return another model; check both exposed projections before release.
            CheckAndBind(initialized);
            return initialized;
        }
    }

    protected override void InitializeModel(IModel model, bool designTime, bool prevalidation)
    {
        // EF has finalized mutable models here and serializes first-time initialization.
        CheckAndBind(model);
        base.InitializeModel(model, designTime, prevalidation);
    }

    private void CheckAndBind(IModel model)
    {
        if (model.FindRuntimeAnnotation(DriverAnnotation)?.Value is DamengDriver existing)
        {
            CheckDriver(model, existing);
        }

        foreach (var entity in model.GetEntityTypes())
        {
            // Flatten includes nested complex properties; entity enumeration includes owned/derived types.
            foreach (var property in entity.GetFlattenedProperties())
            {
                CheckMapping(model, property.FindTypeMapping());
            }
        }

        // The annotation is runtime-only and atomic: simultaneous initializers cannot overwrite identity.
        var owner = model.GetOrAddRuntimeAnnotationValue(DriverAnnotation, static d => d, driver);
        CheckDriver(model, owner!);
    }

    private void CheckMapping(IModel model, CoreTypeMapping? mapping)
    {
        if (mapping is IDamengDriverBoundMapping { Driver: { } owner })
        {
            CheckDriver(model, owner);
        }

        if (mapping?.ElementTypeMapping is { } element)
        {
            CheckMapping(model, element);
        }
    }

    private void CheckDriver(IModel model, DamengDriver owner)
    {
        if (!driver.Equals(owner))
        {
            throw DamengDriverErrors.ForeignModel(driver, owner, model.GetType());
        }
    }
}
