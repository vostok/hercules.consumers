using Vostok.Hercules.Client.Abstractions.Models;
using Vostok.Tracing.Abstractions;
using Vostok.Tracing.Extensions.Custom;

namespace Vostok.Hercules.Consumers.Helpers
{
    internal static class TracerExtensions
    {
        public static ICustomOperationSpanBuilder BeginConsumerCustomOperationSpan(this ITracer tracer, string operationName)
        {
            var spanBuilder = tracer.BeginCustomOperationSpan(operationName);
            spanBuilder.SetOperationStatus(null, WellKnownStatuses.Success);
            spanBuilder.SetTargetDetails("Hercules.Consumers", "default");
            spanBuilder.SetAnnotation(WellKnownAnnotations.Common.Component, "Hercules.Consumers");
            return spanBuilder;
        }

        public static void SetShard(this ICustomOperationSpanBuilder builder, int index, int count) =>
            builder.SetCustomAnnotation("shard", $"{index}/{count}");

        public static void SetStream(this ICustomOperationSpanBuilder builder, string stream) =>
            builder.SetCustomAnnotation("stream", stream);

        public static void SetCoordinates(this ICustomOperationSpanBuilder builder, StreamCoordinates coordinates) =>
            builder.SetCustomAnnotation("coordinates", coordinates.ToString());
    }
}