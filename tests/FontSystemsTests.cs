using Data;
using Data.Messages;
using Data.Systems;
using Simulation.Tests;
using Types;
using Worlds;

namespace Fonts.Systems.Tests
{
    public abstract class FontSystemsTests : SimulationTests
    {
        public World world;

        static FontSystemsTests()
        {
            MetadataRegistry.Load<DataMetadataBank>();
            MetadataRegistry.Load<FontsMetadataBank>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            Schema schema = new();
            schema.Load<DataSchemaBank>();
            schema.Load<FontsSchemaBank>();
            world = new(schema);
            Simulator.Add(new DataImportSystem(Simulator, world));
            Simulator.Add(new FontImportSystem(Simulator, world));
        }

        protected override void TearDown()
        {
            Simulator.Remove<FontImportSystem>();
            Simulator.Remove<DataImportSystem>();
            world.Dispose();
            base.TearDown();
        }

        protected override void Update(double deltaTime)
        {
            Simulator.Broadcast(new DataUpdate(deltaTime));
        }
    }
}
