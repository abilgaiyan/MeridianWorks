using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PulseStack.Abstractions.Assets;
using PulseStack.Abstractions.Persistence.AIAssets.Catalog;
using PulseStack.Abstractions.Persistence.AIAssets.Mapping;
using PulseStack.Abstractions.Persistence.AIAssets.Serialization;
using PulseStack.Abstractions.Persistence.AIAssets.Storage;
using PulseStack.Abstractions.Persistence.AIAssets.Validation;
using PulseStack.Abstractions.Workflows;
using PulseStack.Abstractions.Workflows.Definitions;
using PulseStack.Agents.Builders;
using PulseStack.Core.Assets;
using PulseStack.Core.DependencyInjection;
using PulseStack.Core.Persistence.AIAssets.Catalog;
using PulseStack.Core.Persistence.AIAssets.Storage;
using PulseStack.Providers.OpenRouter.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var apiKey =
    Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? throw new InvalidOperationException(
        "OPENROUTER_API_KEY is not configured.");

var model =
    configuration["OpenRouter:Model"]
    ?? throw new InvalidOperationException(
        "OpenRouter:Model is not configured.");

var services = new ServiceCollection();

services
    .AddPulseStack()
    .UseOpenRouter(
        apiKey: apiKey,
        model: model);

using var serviceProvider =
    services.BuildServiceProvider();

var modelId = StableId("6c87f3c8-b8df-4f8d-bdb0-7f7a310da001");
var promptId = StableId("6c87f3c8-b8df-4f8d-bdb0-7f7a310da002");
var agentId = StableId("6c87f3c8-b8df-4f8d-bdb0-7f7a310da003");
var workflowId = StableId("6c87f3c8-b8df-4f8d-bdb0-7f7a310da004");
var projectId = StableId("6c87f3c8-b8df-4f8d-bdb0-7f7a310da005");

var modelAssetFactory =
    serviceProvider.GetRequiredService<ModelAssetFactory>();

var modelAsset =
    modelAssetFactory.Create(
        modelId,
        new ModelAssetOptions(
            Provider: "OpenRouter",
            Model: model));

var promptAssetFactory =
    serviceProvider.GetRequiredService<PromptAssetFactory>();

var promptAsset =
    promptAssetFactory.Create(
        promptId,
        new PromptAssetOptions
        {
            Name = "RFQ Analysis",
            SystemInstructions =
                """
                You are an RFQ analyst for Meridian Works, an industrial manufacturing company.

                Analyze the customer's RFQ and prepare a concise quotation brief for the sales
                and engineering teams.

                Extract the information that is explicitly provided, including:
                - customer
                - part or component
                - quantity
                - material
                - dimensions
                - delivery requirements

                Identify quotation-critical engineering information that is missing or unclear.
                Consider, where relevant:
                - dimensional tolerances
                - surface-finish requirements
                - heat-treatment requirements
                - drawings or technical specifications

                Do not invent missing engineering information.

                If material information required for a reliable quotation is missing, clearly
                identify the required clarifications and recommend obtaining them before a final
                quotation is prepared.

                Produce a concise, business-readable RFQ analysis.
                """
        });

var agent =
    new AgentBuilder("RFQ Analyst")
        .WithId(agentId)
        .WithRole("Manufacturing RFQ Analyst")
        .WithGoal(
            "Analyze customer RFQs and prepare a quotation brief " +
            "that identifies supplied requirements and missing " +
            "quotation-critical engineering information.")
        .AddResponsibilities(
        [
            "Extract customer and manufacturing requirements from RFQs.",
            "Identify missing or unclear quotation-critical engineering information.",
            "Recommend whether clarification is required before final quotation."
        ])
        .UseModel(Reference(modelAsset))
        .UsePrompt(Reference(promptAsset))
        .Build();

var workflowAssetFactory =
    serviceProvider.GetRequiredService<WorkflowAssetFactory>();

var analyzeRfqStep =
    DurableWorkflowStep.Run(
        new WorkflowStepId(
            Guid.Parse("6c87f3c8-b8df-4f8d-bdb0-7f7a310da006")),
        Reference(agent));

var workflow =
    workflowAssetFactory.Create(
        workflowId,
        new IdentityCompleteWorkflowAssetOptions
        {
            Name = "Analyze RFQ",
            Description =
                "Analyze a customer RFQ and prepare a quotation brief.",
            Steps =
            [
                analyzeRfqStep
            ]
        });

var projectAssetFactory =
    serviceProvider.GetRequiredService<ProjectAssetFactory>();

var project =
    projectAssetFactory.Create(
        projectId,
        new ProjectAssetOptions
        {
            Name = "Meridian Works",
            Description =
                "AI-assisted RFQ intake and analysis for industrial manufacturing.",
            EntryWorkflow = Reference(workflow),
            OwnedAssets =
            [
                Reference(workflow),
                Reference(agent),
                Reference(promptAsset),
                Reference(modelAsset)
            ]
        });

Console.WriteLine("Meridian Works — declarative model proof");
Console.WriteLine($"Type: {modelAsset.Type}");
Console.WriteLine($"Provider: {modelAsset.Options.Provider}");
Console.WriteLine($"Model: {modelAsset.Options.Model}");
Console.WriteLine($"URN: {modelAsset.Urn}");

Console.WriteLine();
Console.WriteLine("RFQ Analysis Prompt");
Console.WriteLine($"Type: {promptAsset.Type}");
Console.WriteLine($"Name: {promptAsset.Options.Name}");
Console.WriteLine($"URN: {promptAsset.Urn}");

Console.WriteLine();
Console.WriteLine("RFQ Analyst Agent");
Console.WriteLine($"Type: {agent.Type}");
Console.WriteLine($"Name: {agent.Options.Name}");
Console.WriteLine($"Role: {agent.Options.Role}");
Console.WriteLine($"URN: {agent.Urn}");
Console.WriteLine($"Model: {agent.Options.Model?.Urn}");
Console.WriteLine($"Prompt: {agent.Options.Prompt?.Urn}");

Console.WriteLine();
Console.WriteLine("Analyze RFQ Workflow");
Console.WriteLine($"Type: {workflow.Type}");
Console.WriteLine($"Name: {workflow.Options.Name}");
Console.WriteLine($"URN: {workflow.Urn}");
Console.WriteLine($"Steps: {workflow.Options.Steps.Count}");

var runStep =
    (RunStepDefinition)workflow.Options.Steps.Single();

Console.WriteLine($"Run Agent: {runStep.Agent.Urn}");

Console.WriteLine();
Console.WriteLine("Meridian Works Project");
Console.WriteLine($"Type: {project.Type}");
Console.WriteLine($"Name: {project.Options.Name}");
Console.WriteLine($"URN: {project.Urn}");
Console.WriteLine($"Entry Workflow: {project.Options.EntryWorkflow.Urn}");
Console.WriteLine($"Owned Assets: {project.Options.OwnedAssets.Count}");

foreach (var ownedAsset in project.Options.OwnedAssets)
{
    Console.WriteLine(
        $"  {ownedAsset.Type}: {ownedAsset.Urn}");
}

VerifyDeclarativeGraph(
    project,
    workflow,
    agent,
    promptAsset,
    modelAsset);

Console.WriteLine();
Console.WriteLine(
    "Meridian Works V1 declarative graph: VERIFIED");

var persistenceRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MeridianWorks",
    "PulseStackAI");

var store = new FileSerializedAIAssetStore(
    Path.Combine(persistenceRoot, "assets"));
var catalog = new FileAIAssetCatalogProvider(
    Path.Combine(persistenceRoot, "catalog"));
var codec = new AIAssetDocumentCodec();
var validator =
    serviceProvider.GetRequiredService<IAIAssetDocumentValidator>();
var mapper =
    serviceProvider.GetRequiredService<IAIAssetDocumentMapper>();
var storageOptions = new AIAssetStorageOptions
{
    MaximumRepresentationSizeBytes = 4 * 1024 * 1024
};
var writer = new AIAssetWriter(
    store,
    codec,
    validator,
    storageOptions);
var loader = new AIAssetLoader(
    store,
    codec,
    validator,
    mapper,
    storageOptions);
var publisher = new AIAssetPublisher(
    catalog,
    loader);

IAsset[] definitions =
[
    modelAsset,
    promptAsset,
    agent,
    workflow,
    project
];

foreach (var definition in definitions)
{
    var key = DefinitionKey(definition);
    var document = mapper.ToDocument(definition);
    var result = await writer.WriteAsync(key, document);

    if (result is not (AIAssetWriteResult.Created or AIAssetWriteResult.AlreadyPresent))
    {
        throw new InvalidOperationException(
            $"Persistence failed for {definition.Type} '{definition.Urn}': {result}.");
    }

    Console.WriteLine(
        $"Stored {definition.Type}: {result}");
}

foreach (var definition in definitions)
{
    var key = DefinitionKey(definition);
    var result = await publisher.PublishAsync(key);

    if (result is not (AIAssetPublicationResult.Published or AIAssetPublicationResult.AlreadyPublished))
    {
        throw new InvalidOperationException(
            $"Publication failed for {definition.Type} '{definition.Urn}': {result}.");
    }

    Console.WriteLine(
        $"Published {definition.Type}: {result}");
}

Console.WriteLine();
Console.WriteLine(
    "Meridian Works V1 declarative application: PERSISTED + PUBLISHED");

static AssetId StableId(string value) =>
    new(Guid.Parse(value));

static AssetDefinitionKey DefinitionKey(IAsset asset) =>
    new(
        asset.Type,
        asset.Id,
        asset.Version);

static AssetReference Reference(IAsset asset) =>
    new(
        asset.Type,
        asset.Id,
        asset.Urn,
        asset.Version);

static void VerifyDeclarativeGraph(
    ProjectAsset project,
    WorkflowAsset workflow,
    AgentDefinition agent,
    PromptAsset prompt,
    ModelAsset model)
{
    if (project.Options.EntryWorkflow != Reference(workflow))
    {
        throw new InvalidOperationException(
            "Project entry Workflow does not match Analyze RFQ.");
    }

    if (project.Options.OwnedAssets.Count != 4)
    {
        throw new InvalidOperationException(
            "Project must own exactly four V1 assets.");
    }

    var expectedOwnedAssets = new[]
    {
        Reference(workflow),
        Reference(agent),
        Reference(prompt),
        Reference(model)
    };

    if (!project.Options.OwnedAssets.SequenceEqual(expectedOwnedAssets))
    {
        throw new InvalidOperationException(
            "Project owned assets do not match the canonical V1 graph.");
    }

    if (workflow.Options.Steps.Count != 1)
    {
        throw new InvalidOperationException(
            "Analyze RFQ must contain exactly one step.");
    }

    if (workflow.Options.Steps.Single() is not RunStepDefinition run)
    {
        throw new InvalidOperationException(
            "Analyze RFQ must contain exactly one Run step.");
    }

    if (run.Agent != Reference(agent))
    {
        throw new InvalidOperationException(
            "Analyze RFQ does not reference RFQ Analyst.");
    }

    if (agent.Options.Prompt != Reference(prompt))
    {
        throw new InvalidOperationException(
            "RFQ Analyst does not reference RFQ Analysis prompt.");
    }

    if (agent.Options.Model != Reference(model))
    {
        throw new InvalidOperationException(
            "RFQ Analyst does not reference the configured model.");
    }

    if (agent.Options.Knowledge.Count != 0 ||
        agent.Options.Tools.Count != 0 ||
        agent.Options.Memory is not null ||
        agent.Options.Policies.Count != 0)
    {
        throw new InvalidOperationException(
            "RFQ Analyst contains capabilities outside the Meridian Works V1 boundary.");
    }
}
