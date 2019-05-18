using System;
using JetBrains.Annotations;
using Vostok.ZooKeeper.Client.Abstractions;

namespace Vostok.Hercules.Consumers
{
    [PublicAPI]
    public class ZooKeeperCoordinatesStorageSettings
    {
        public ZooKeeperCoordinatesStorageSettings([NotNull] IZooKeeperClient client, [NotNull] string nodePath)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            NodePath = nodePath ?? throw new ArgumentNullException(nameof(nodePath));
        }

        [NotNull]
        public IZooKeeperClient Client { get; }

        [NotNull]
        public string NodePath { get; }
    }
}