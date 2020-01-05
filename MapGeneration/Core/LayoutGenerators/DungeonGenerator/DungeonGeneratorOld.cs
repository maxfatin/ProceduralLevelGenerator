﻿using System;
using System.Collections.Generic;
using System.Threading;
using GeneralAlgorithms.Algorithms.Common;
using GeneralAlgorithms.Algorithms.Polygons;
using GeneralAlgorithms.DataStructures.Common;
using GeneralAlgorithms.DataStructures.Polygons;
using MapGeneration.Core.ChainDecompositions;
using MapGeneration.Core.Configurations;
using MapGeneration.Core.Configurations.EnergyData;
using MapGeneration.Core.ConfigurationSpaces;
using MapGeneration.Core.Constraints;
using MapGeneration.Core.Doors;
using MapGeneration.Core.GeneratorPlanners;
using MapGeneration.Core.LayoutConverters;
using MapGeneration.Core.LayoutEvolvers.SimulatedAnnealing;
using MapGeneration.Core.LayoutOperations;
using MapGeneration.Core.Layouts;
using MapGeneration.Core.MapDescriptions;
using MapGeneration.Interfaces.Core.Constraints;
using MapGeneration.Interfaces.Core.LayoutGenerator;
using MapGeneration.Interfaces.Core.MapLayouts;
using MapGeneration.Interfaces.Utils;
using MapGeneration.Utils;

namespace MapGeneration.Core.LayoutGenerators.DungeonGenerator
{
    public class DungeonGeneratorOld<TNode> : IRandomInjectable, ICancellable
    {
        private readonly MapDescriptionOld<TNode> mapDescriptionOld;
        private readonly DungeonGeneratorConfiguration configuration;
        private SimpleChainBasedGenerator<MapDescriptionOld<TNode>, Layout<Configuration<CorridorsData>>, IMapLayout<TNode>, int> generator;

        public event EventHandler<SimulatedAnnealingEventArgs> OnSimulatedAnnealingEvent;

        public DungeonGeneratorOld(MapDescriptionOld<TNode> mapDescriptionOld, DungeonGeneratorConfiguration configuration = null)
        {
            this.mapDescriptionOld = mapDescriptionOld;
            this.configuration = configuration ?? new DungeonGeneratorConfiguration(mapDescriptionOld);
            SetupGenerator();
        }

        // TODO: remove
        public double TimeTotal => generator.TimeTotal;

        public int IterationsCount => generator.IterationsCount;

        private void SetupGenerator()
        {
            var chains = configuration.Chains;

            var generatorPlanner = new GeneratorPlanner<Layout<Configuration<CorridorsData>>, int>();

            var configurationSpacesGenerator = new ConfigurationSpacesGeneratorOld(
                new PolygonOverlap(),
                DoorHandler.DefaultHandler,
                new OrthogonalLineIntersection(),
                new GridPolygonUtils());
            var configurationSpaces = configurationSpacesGenerator.Generate<TNode, Configuration<CorridorsData>>(mapDescriptionOld);
            var corridorConfigurationSpaces = mapDescriptionOld.IsWithCorridors ? configurationSpacesGenerator.Generate<TNode, Configuration<CorridorsData>>(mapDescriptionOld, mapDescriptionOld.CorridorsOffsets) : configurationSpaces;

            var averageSize = configurationSpaces.GetAverageSize();

            var constraints = new List<INodeConstraint<Layout<Configuration<CorridorsData>>, int, Configuration<CorridorsData>, CorridorsData>>();

            constraints.Add(new BasicContraint<Layout<Configuration<CorridorsData>>, int, Configuration<CorridorsData>, CorridorsData, IntAlias<GridPolygon>>(
                new FastPolygonOverlap(),
                averageSize,
                configurationSpaces
            ));

            if (mapDescriptionOld.IsWithCorridors)
            {
                constraints.Add(new CorridorConstraintsOld<Layout<Configuration<CorridorsData>>, int, Configuration<CorridorsData>, CorridorsData, IntAlias<GridPolygon>>(
                    mapDescriptionOld,
                    averageSize,
                    corridorConfigurationSpaces
                ));

                //if (!false) // TODO:
                //{
                //    var polygonOverlap = new FastPolygonOverlap();
                //    constraints.Add(new TouchingConstraintsOld<Layout<Configuration<CorridorsData>>, int, Configuration<CorridorsData>, CorridorsData, IntAlias<GridPolygon>>(
                //        mapDescriptionOld,
                //        polygonOverlap
                //    ));
                //}
            }

            var constraintsEvaluator = new ConstraintsEvaluator<Layout<Configuration<CorridorsData>>, int, Configuration<CorridorsData>, IntAlias<GridPolygon>, CorridorsData>(constraints);

            var layoutOperations = new LayoutOperationsOld<Layout<Configuration<CorridorsData>>, int, Configuration<CorridorsData>, IntAlias<GridPolygon>, CorridorsData>(corridorConfigurationSpaces, configurationSpaces.GetAverageSize(), mapDescriptionOld, configurationSpaces, constraintsEvaluator);

            var initialLayout = new Layout<Configuration<CorridorsData>>(mapDescriptionOld.GetGraph());
            var layoutConverter =
                new BasicLayoutConverterOld<Layout<Configuration<CorridorsData>>, TNode,
                    Configuration<CorridorsData>>(mapDescriptionOld, configurationSpaces,
                    configurationSpacesGenerator.LastIntAliasMapping);



            var layoutEvolver =
                    new SimulatedAnnealingEvolver<Layout<Configuration<CorridorsData>>, int,
                    Configuration<CorridorsData>>(layoutOperations, configuration.SimulatedAnnealingConfiguration, true);

            generator = new SimpleChainBasedGenerator<MapDescriptionOld<TNode>, Layout<Configuration<CorridorsData>>, IMapLayout<TNode>, int>(initialLayout, generatorPlanner, chains, layoutEvolver, layoutConverter);

            generator.OnRandomInjected += (random) =>
            {
                ((IRandomInjectable)configurationSpaces).InjectRandomGenerator(random);
                ((IRandomInjectable)layoutOperations).InjectRandomGenerator(random);
                ((IRandomInjectable)layoutEvolver).InjectRandomGenerator(random);
                ((IRandomInjectable)layoutConverter).InjectRandomGenerator(random);
            };

            generator.OnCancellationTokenInjected += (token) =>
            {
                ((ICancellable)generatorPlanner).SetCancellationToken(token);
                ((ICancellable)layoutEvolver).SetCancellationToken(token);
            };
            
            layoutEvolver.OnEvent += (sender, args) => OnSimulatedAnnealingEvent?.Invoke(sender, args);
        }

        public IMapLayout<TNode> GenerateLayout()
        {
            var layout = generator.GenerateLayout();

            return layout;
        }

        public void InjectRandomGenerator(Random random)
        {
            generator.InjectRandomGenerator(random);
        }

        public void SetCancellationToken(CancellationToken? cancellationToken)
        {
            generator.SetCancellationToken(cancellationToken);
        }
    }
}