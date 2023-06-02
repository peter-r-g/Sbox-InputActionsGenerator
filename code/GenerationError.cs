namespace InputActionsGenerator;

/// <summary>
/// Represents a type of error that the generator can run into.
/// </summary>
internal enum GenerationError
{
	None,
	NoInputSettings,
	ParseActionsFailed
}
