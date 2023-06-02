using Editor;
using Sandbox;
using System.Diagnostics;

namespace InputActionsGenerator;

/// <summary>
/// A notice for when code is being generated.
/// </summary>
internal sealed class GeneratingNotice : NoticeWidget
{
	/// <summary>
	/// The project that is having code generated.
	/// </summary>
	internal LocalProject Project { get; }
	/// <summary>
	/// The current stage of the generation process.
	/// </summary>
	internal GenerationStage CurrentStage { get; set; } = GenerationStage.LookingForActions;
	/// <summary>
	/// The error that the generation process came into.
	/// </summary>
	internal GenerationError Error { get; set; } = GenerationError.None;

	internal GeneratingNotice( LocalProject project )
	{
		Position = 10;
		Project = project;
		Reset();
	}

	/// <inheritdoc/>
	public override void Reset()
	{
		base.Reset();

		IsRunning = true;
		Tick();
		SetBodyWidget( null );

		FixedWidth = 320;
		FixedHeight = 76;
		Title = "Generating for " + Project.Package.Title;
		Icon = MaterialIcon.BuildCircle;
		BorderColor = Theme.Primary;
		CurrentStage = GenerationStage.LookingForActions;
		Error = GenerationError.None;
	}

	/// <inheritdoc/>
	public override void Tick()
	{
		if ( !IsRunning )
			return;

		if ( Error != GenerationError.None )
		{
			IsRunning = false;
			Icon = MaterialIcon.Error;
			BorderColor = Color.Red;
			Title = "Generation failed for " + Project.Package.Title;
			Subtitle = Error switch
			{
				GenerationError.NoInputSettings => "Failed to find input settings in .addon file",
				_ => throw new UnreachableException()
			};

			NoticeManager.Remove( this, 5 );
			return;
		}

		IsRunning = CurrentStage != GenerationStage.Finished;
		if ( IsRunning )
		{
			Subtitle = CurrentStage switch
			{
				GenerationStage.LookingForActions => "Looking for input actions...",
				GenerationStage.ParsingActions => "Parsing input actions...",
				GenerationStage.FindingRootNamespace => "Finding root namespace...",
				GenerationStage.OpeningGeneratedFile => "Opening file for writing...",
				GenerationStage.GeneratingCode => "Generating code...",
				_ => throw new UnreachableException()
			};
		}
		else
		{
			Icon = MaterialIcon.Done;
			BorderColor = Theme.Green;
			Title = "Generation completed for " + Project.Package.Title;
			Subtitle = string.Empty;

			NoticeManager.Remove( this );
		}
	}
}
