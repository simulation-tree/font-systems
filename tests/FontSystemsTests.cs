using Data;
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
            TypeRegistry.Load<DataTypeBank>();
            TypeRegistry.Load<FontsTypeBank>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            simulator.AddSystem(new DataImportSystem());
            simulator.AddSystem(new FontImportSystem());
        }

        protected override Schema CreateSchema()
        {
            Schema schema = base.CreateSchema();
            schema.Load<DataSchemaBank>();
            schema.Load<FontsSchemaBank>();
            return schema;
        }
    }
}
