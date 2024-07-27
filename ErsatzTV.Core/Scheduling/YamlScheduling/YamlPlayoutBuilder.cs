using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Metadata;
using ErsatzTV.Core.Interfaces.Repositories;
using ErsatzTV.Core.Interfaces.Scheduling;
using ErsatzTV.Core.Scheduling.YamlScheduling.Handlers;
using ErsatzTV.Core.Scheduling.YamlScheduling.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ErsatzTV.Core.Scheduling.YamlScheduling;

public class YamlPlayoutBuilder(
    ILocalFileSystem localFileSystem,
    IConfigElementRepository configElementRepository,
    IMediaCollectionRepository mediaCollectionRepository,
    ILogger<YamlPlayoutBuilder> logger)
    : IYamlPlayoutBuilder
{
    public async Task<Playout> Build(Playout playout, PlayoutBuildMode mode, CancellationToken cancellationToken)
    {
        if (!localFileSystem.FileExists(playout.TemplateFile))
        {
            logger.LogWarning("YAML playout file {File} does not exist; aborting.", playout.TemplateFile);
            return playout;
        }

        YamlPlayoutDefinition playoutDefinition = await LoadYamlDefinition(playout, cancellationToken);

        DateTimeOffset start = DateTimeOffset.Now;
        int daysToBuild = await GetDaysToBuild();
        DateTimeOffset finish = start.AddDays(daysToBuild);

        if (mode is not PlayoutBuildMode.Reset)
        {
            logger.LogWarning("YAML playouts can only be reset; ignoring build mode {Mode}", mode.ToString());
            return playout;
        }

        // load content and content enumerators on demand
        Dictionary<YamlPlayoutInstruction, IYamlPlayoutHandler> handlers = new();
        var enumeratorCache = new EnumeratorCache(mediaCollectionRepository);

        var context = new YamlPlayoutContext(playout, playoutDefinition)
        {
            CurrentTime = start,
            GuideGroup = 1,
            InstructionIndex = 0
        };

        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (mode is PlayoutBuildMode.Reset)
        {
            context.Playout.Seed = new Random().Next();
            context.Playout.Items.Clear();

            // handle all on-reset instructions
            foreach (YamlPlayoutInstruction instruction in playoutDefinition.Reset)
            {
                Option<IYamlPlayoutHandler> maybeHandler = GetHandlerForInstruction(
                    handlers,
                    enumeratorCache,
                    instruction);

                foreach (IYamlPlayoutHandler handler in maybeHandler)
                {
                    if (!handler.Reset)
                    {
                        logger.LogInformation(
                            "Skipping unsupported reset instruction {Instruction}",
                            instruction.GetType().Name);
                    }
                    else
                    {
                        await handler.Handle(context, instruction, logger, cancellationToken);
                    }
                }
            }
        }

        // handle all playout instructions
        while (context.CurrentTime < finish)
        {
            if (context.InstructionIndex >= playoutDefinition.Playout.Count)
            {
                logger.LogInformation("Reached the end of the YAML playout definition; stopping");
                break;
            }

            YamlPlayoutInstruction instruction = playoutDefinition.Playout[context.InstructionIndex];
            Option<IYamlPlayoutHandler> maybeHandler = GetHandlerForInstruction(handlers, enumeratorCache, instruction);

            foreach (IYamlPlayoutHandler handler in maybeHandler)
            {
                if (!await handler.Handle(context, instruction, logger, cancellationToken))
                {
                    logger.LogInformation("YAML playout instruction handler failed");
                }
            }

            if (!instruction.ChangesIndex)
            {
                context.InstructionIndex++;
            }
        }

        return playout;
    }

    private async Task<int> GetDaysToBuild() =>
        await configElementRepository
            .GetValue<int>(ConfigElementKey.PlayoutDaysToBuild)
            .IfNoneAsync(2);

    private static Option<IYamlPlayoutHandler> GetHandlerForInstruction(
        Dictionary<YamlPlayoutInstruction, IYamlPlayoutHandler> handlers,
        EnumeratorCache enumeratorCache,
        YamlPlayoutInstruction instruction)
    {
        if (handlers.TryGetValue(instruction, out IYamlPlayoutHandler handler))
        {
            return Optional(handler);
        }

        handler = instruction switch
        {
            YamlPlayoutRepeatInstruction => new YamlPlayoutRepeatHandler(),
            YamlPlayoutWaitUntilInstruction => new YamlPlayoutWaitUntilHandler(),
            YamlPlayoutNewEpgGroupInstruction => new YamlPlayoutNewEpgGroupHandler(),
            YamlPlayoutSkipItemsInstruction => new YamlPlayoutSkipItemsHandler(),

            // content handlers
            YamlPlayoutCountInstruction => new YamlPlayoutCountHandler(enumeratorCache),
            YamlPlayoutDurationInstruction => new YamlPlayoutDurationHandler(enumeratorCache),
            YamlPlayoutPadToNextInstruction => new YamlPlayoutPadToNextHandler(enumeratorCache),

            _ => null
        };

        if (handler != null)
        {
            handlers.Add(instruction, handler);
        }

        return Optional(handler);
    }

    private static async Task<YamlPlayoutDefinition> LoadYamlDefinition(Playout playout, CancellationToken cancellationToken)
    {
        string yaml = await File.ReadAllTextAsync(playout.TemplateFile, cancellationToken);

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeDiscriminatingNodeDeserializer(
                o =>
                {
                    var contentKeyMappings = new Dictionary<string, Type>
                    {
                        { "search", typeof(YamlPlayoutContentSearchItem) },
                        { "show", typeof(YamlPlayoutContentShowItem) }
                    };

                    o.AddUniqueKeyTypeDiscriminator<YamlPlayoutContentItem>(contentKeyMappings);

                    var instructionKeyMappings = new Dictionary<string, Type>
                    {
                        { "count", typeof(YamlPlayoutCountInstruction) },
                        { "duration", typeof(YamlPlayoutDurationInstruction) },
                        { "new_epg_group", typeof(YamlPlayoutNewEpgGroupInstruction) },
                        { "pad_to_next", typeof(YamlPlayoutPadToNextInstruction) },
                        { "repeat", typeof(YamlPlayoutRepeatInstruction) },
                        { "skip_items", typeof(YamlPlayoutSkipItemsInstruction) },
                        { "wait_until", typeof(YamlPlayoutWaitUntilInstruction) }
                    };

                    o.AddUniqueKeyTypeDiscriminator<YamlPlayoutInstruction>(instructionKeyMappings);
                })
            .Build();

        return deserializer.Deserialize<YamlPlayoutDefinition>(yaml);
    }
}