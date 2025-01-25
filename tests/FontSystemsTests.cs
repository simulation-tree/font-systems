using Data.Systems;
using Simulation.Tests;
using Types;
using Worlds;

namespace Fonts.Systems.Tests
{
    public abstract class FontSystemsTests : SimulationTests
    {
        static FontSystemsTests()
        {
            TypeRegistry.Load<Data.TypeBank>();
            TypeRegistry.Load<Fonts.TypeBank>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            simulator.AddSystem<DataImportSystem>();
            simulator.AddSystem<FontImportSystem>();
        }

        protected override Schema CreateSchema()
        {
            Schema schema = base.CreateSchema();
            schema.Load<Data.SchemaBank>();
            schema.Load<Fonts.SchemaBank>();
            return schema;
        }
    }
}
