using Vostok.Tracing.Abstractions;
using Vostok.Tracing.Extensions.Custom;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class TracerExtensions
    {
        public static ICustomOperationSpanBuilder BeginConsumerCustomOperationSpan(this ITracer tracer, string operationName)
        {
            var spanBuilder = tracer.BeginCustomOperationSpan(operationName);
            spanBuilder.SetTargetDetails("Hercules.Consumers", "default");
            spanBuilder.SetAnnotation(WellKnownAnnotations.Common.Component, "Hercules.Consumers");
            return spanBuilder;
        }

        public static void SetCustomAnnotation(this ICustomOperationSpanBuilder builder, string key, string value)
            => builder.SetAnnotation($"custom.{key}", value);

        public static void SetSuccess(this ICustomOperationSpanBuilder builder) 
            => builder.SetOperationStatus(null, WellKnownStatuses.Success);
    }
}