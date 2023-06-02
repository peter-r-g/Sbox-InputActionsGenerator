namespace InputActionsGenerator;

/// <summary>
/// Represents a stage in the code generation process.
/// </summary>
internal enum GenerationStage
{
	LookingForActions,
	ParsingActions,
	FindingRootNamespace,
	OpeningGeneratedFile,
	GeneratingCode,
	Finished
}
