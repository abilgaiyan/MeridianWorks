using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PulseStack.Abstractions.Assets;
using PulseStack.Abstractions.Chat;
using PulseStack.Abstractions.Knowledge;
using PulseStack.Abstractions.Models;
using PulseStack.Abstractions.Persistence.AIAssets.Catalog;
using PulseStack.Abstractions.Persistence.AIAssets.Mapping;
using PulseStack.Abstractions.Persistence.AIAssets.Storage;
using PulseStack.Abstractions.Runtime.Application;
using PulseStack.Abstractions.Runtime.Invocation.Application;
using PulseStack.Abstractions.Runtime.Realization.Binding;
using PulseStack.Abstractions.Workflows;
using PulseStack.Abstractions.Workflows.Definitions;
using PulseStack.Agents.Builders;
using PulseStack.Agents.DependencyInjection;
using PulseStack.Core.Assets;
using PulseStack.Core.DependencyInjection;

internal static class KnowledgeRestartProof
{
    private const string Provider = "MeridianProof";
    private const string Model = "recording-client";
    private const string Input = "RFQ: 250 EN8 drive shafts. Identify quotation prerequisites.";
    private const string Output = "Knowledge provider-boundary conformance verified.";
    private static readonly string[] Names = ["Engineering standards", "Quotation policy"];
    private static readonly string[][] Material =
    [
        ["Require drawing revision MW-ENG-17.", "Confirm shaft tolerance before quotation."],
        ["Quotation requires engineering approval MW-SALES-04."]
    ];

    internal static async Task RunAsync(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("Usage: knowledge-prepare|knowledge-execute <new-proof-directory>");
        var prepare = args[0] == "knowledge-prepare";
        var root = Path.GetFullPath(args[1]);
        var recordPath = Path.Combine(root, "knowledge-prepared.json");
        if (prepare && Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("Knowledge preparation requires an empty dedicated directory.");
        if (!prepare && !File.Exists(recordPath))
            throw new InvalidOperationException("Knowledge preparation record is unavailable.");

        var client = new RecordingClient();
        var sources = Names.Select((name, index) => new ReferenceSource(name, Material[index], client)).ToArray();
        var services = new ServiceCollection();
        services.AddPulseStack().AddPulseStackAgents().AddPulseStackWorkflows()
            .AddFileAIAssetStorage(Path.Combine(root, "assets"),
                new AIAssetStorageOptions { MaximumRepresentationSizeBytes = 4 * 1024 * 1024 })
            .AddFileAIAssetCatalog(Path.Combine(root, "catalog"))
            .AddAIAssetGraphLoading();
        services.AddSingleton<IModelCatalog, ProofModelCatalog>();
        services.AddSingleton(new ChatClientFactoryRegistration(Provider, new RecordingFactory(client)));
        services.AddSingleton(new KnowledgeExecutionOptions { MaxContributionBytes = 65536 });
        foreach (var source in sources)
            services.AddSingleton<IKnowledgeSource>(source);

        Preparation record;
        if (prepare)
        {
            // Authoring uses public factories only. Knowledge factory owns its identity.
            var knowledgeFactory = new KnowledgeAssetFactory();
            var knowledge = Names.Select(name => knowledgeFactory.Create(new KnowledgeAssetOptions
            {
                Name = name, Description = "Consumer-owned RFQ reference material."
            })).ToArray();
            var model = new ModelAssetFactory(new ProofModelCatalog()).Create(new ModelAssetOptions(Provider, Model));
            var agent = new AgentBuilder("Knowledge RFQ Analyst")
                .WithRole("RFQ Analyst").WithGoal("Identify quotation prerequisites.")
                .UseModel(Reference(model))
                .UseKnowledge(Reference(knowledge[0])).UseKnowledge(Reference(knowledge[1])).Build();
            var workflow = new WorkflowAssetFactory().Create(new IdentityCompleteWorkflowAssetOptions
            {
                Name = "Knowledge RFQ proof", Description = "Execute a persisted Knowledge-bound agent.",
                Steps = [DurableWorkflowStep.Run(new WorkflowStepId(Guid.NewGuid()), Reference(agent))]
            });
            var project = new ProjectAssetFactory().Create(new ProjectAssetOptions
            {
                Name = "Meridian Knowledge proof", Description = "External package conformance.",
                EntryWorkflow = Reference(workflow),
                OwnedAssets = [Reference(workflow), Reference(agent), Reference(model),
                    Reference(knowledge[0]), Reference(knowledge[1])]
            });
            record = new Preparation(1, Environment.ProcessId, Reference(project),
                Reference(workflow), knowledge.Select(Reference).ToArray());
            using var provider = services.BuildServiceProvider();
            var mapper = provider.GetRequiredService<IAIAssetDocumentMapper>();
            var writer = provider.GetRequiredService<IAIAssetWriter>();
            var publisher = provider.GetRequiredService<IAIAssetPublisher>();
            IAsset[] definitions = [model, knowledge[0], knowledge[1], agent, workflow, project];
            foreach (var asset in definitions)
            {
                var writeResult = await writer.WriteAsync(Key(Reference(asset)), mapper.ToDocument(asset));
                if (writeResult is not (AIAssetWriteResult.Created or AIAssetWriteResult.AlreadyPresent))
                    throw new InvalidOperationException($"Knowledge preparation store failed: {writeResult}.");
            }
            foreach (var asset in definitions)
            {
                var publicationResult = await publisher.PublishAsync(Key(Reference(asset)));
                if (publicationResult is not (AIAssetPublicationResult.Published or AIAssetPublicationResult.AlreadyPublished))
                    throw new InvalidOperationException($"Knowledge preparation publication failed: {publicationResult}.");
            }
            if (client.Calls != 0 || sources.Any(source => source.Calls != 0))
                throw new InvalidOperationException("Preparation performed execution.");
            using (var file = new FileStream(recordPath, FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(file, record, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"Process {Environment.ProcessId}: KNOWLEDGE PREPARED; no invocation.");
            return;
        }

        record = JsonSerializer.Deserialize<Preparation>(File.ReadAllText(recordPath))
            ?? throw new InvalidOperationException("Preparation record is null.");
        if (record.SchemaVersion != 1 || record.PreparationProcessId == Environment.ProcessId ||
            record.Project.Type != AssetType.Project || record.EntryWorkflow.Type != AssetType.Workflow ||
            record.Knowledge.Length != sources.Length || record.Knowledge.Any(reference => reference.Type != AssetType.Knowledge))
            throw new InvalidOperationException("Invalid preparation record or process boundary.");
        for (var index = 0; index < sources.Length; index++)
            services.AddSingleton(new KnowledgeBindingRegistration(record.Knowledge[index], sources[index].Name));

        var before = Snapshot(root);
        using var executionProvider = services.BuildServiceProvider();
        using var scope = executionProvider.CreateScope();
        var operation = scope.ServiceProvider.GetRequiredService<IApplicationOperation>();
        using var cancellation = new CancellationTokenSource();
        var result = await operation.ExecuteAsync(Key(record.Project),
            new ApplicationInvocationRequest(Input), cancellation.Token);
        if (result is not ApplicationOperationResult.InvocationOutcome outcome ||
            !outcome.Result.Success || outcome.Result.Project != record.Project ||
            outcome.Result.EntryWorkflow != record.EntryWorkflow || outcome.Result.FinalOutput != Output ||
            client.Calls != 1 || sources.Any(source => source.Calls != 1))
            throw new InvalidOperationException($"Knowledge execution failed: {result.GetType().Name}.");
        if (!before.SequenceEqual(Snapshot(root)))
            throw new InvalidOperationException("Knowledge execution modified durable contents.");
        var evidence = new
        {
            schemaVersion = 1,
            frameworkSource = "c37cdd354b971aa2649fd4296dce2daa6053120e",
            preparationProcessId = record.PreparationProcessId,
            executionProcessId = Environment.ProcessId,
            project = record.Project,
            sourceCalls = sources.Select(source => new { source.Name, source.Calls }),
            providerCalls = client.Calls,
            persistenceUnchanged = true,
            finalOutput = outcome.Result.FinalOutput
        };
        using (var file = new FileStream(Path.Combine(root, "knowledge-executed.json"), FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(file, evidence, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Process {Environment.ProcessId}: KNOWLEDGE RESTART VERIFIED; persistence unchanged.");
    }

    private static AssetReference Reference(IAsset asset) => new(asset.Type, asset.Id, asset.Urn, asset.Version);
    private static AssetDefinitionKey Key(AssetReference asset) => new(asset.Type, asset.Id, asset.Version);
    private static string[] Snapshot(string root) =>
        new[] { "assets", "catalog" }.SelectMany(name =>
            Directory.EnumerateFiles(Path.Combine(root, name), "*", SearchOption.AllDirectories))
            .Append(Path.Combine(root, "knowledge-prepared.json"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + ":" +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private sealed record Preparation(int SchemaVersion, int PreparationProcessId,
        AssetReference Project, AssetReference EntryWorkflow, AssetReference[] Knowledge);

    private sealed class ProofModelCatalog : IModelCatalog
    {
        public IReadOnlyCollection<ProviderModelDescriptor> GetModels() => [new(Provider, Model)];
        public bool Contains(string provider, string model) => provider == Provider && model == Model;
    }

    private sealed class ReferenceSource(string name, string[] material, RecordingClient client) : IKnowledgeSource
    {
        public string Name => name;
        public int Calls { get; private set; }
        public Task<KnowledgeResult> RetrieveAsync(KnowledgeQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.Text != Input)
                throw new InvalidOperationException("Knowledge query changed.");
            client.ObserveSource(Name, cancellationToken);
            Calls++;
            return Task.FromResult(new KnowledgeResult { Items = material });
        }
    }

    private sealed class RecordingFactory(RecordingClient client) : IChatClientFactory
    {
        public IChatClient Create(string model) => model == Model
            ? client : throw new InvalidOperationException("Unexpected proof model.");
    }

    private sealed class RecordingClient : IChatClient
    {
        public int Calls { get; private set; }
        private CancellationToken? _retrievalToken;
        private int _retrievedSources;
        public void ObserveSource(string name, CancellationToken token)
        {
            if (_retrievedSources >= Names.Length || name != Names[_retrievedSources] ||
                (_retrievalToken.HasValue && _retrievalToken.Value != token))
                throw new InvalidOperationException("Source order or effective runtime token changed.");
            _retrievalToken = token;
            _retrievedSources++;
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = messages.ToArray();
            var expected = "Retrieved reference material\n\nSource: " + Names[0] + "\n" +
                string.Join("\n\n", Material[0]) + "\n\nSource: " + Names[1] + "\n" +
                string.Join("\n\n", Material[1]) + "\n";
            if (_retrievedSources != Names.Length || cancellationToken != _retrievalToken || snapshot.Length != 2 ||
                snapshot[0].Role != ChatRole.User || snapshot[0].Text != expected ||
                snapshot[1].Role != ChatRole.User || snapshot[1].Text != Input)
                throw new InvalidOperationException("Provider did not receive exact ordered User-role Knowledge and input.");
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Output)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This proof targets application invocation.");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
