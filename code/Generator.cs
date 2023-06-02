using Editor;
using Sandbox;
using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace InputActionsGenerator;

/// <summary>
/// Generates a C# file containing input action data for a gamemode.
/// </summary>
internal static class Generator
{
	/// <summary>
	/// The options to give to the <see cref="InputAction"/> deserialize calls.
	/// </summary>
	private static JsonSerializerOptions JsonOptions
	{
		get
		{
			if ( jsonOptions is null )
			{
				jsonOptions = new JsonSerializerOptions();
				jsonOptions.Converters.Add( new CodeConverter() );
			}

			return jsonOptions;
		}
	}
	private static JsonSerializerOptions? jsonOptions;

	/// <summary>
	/// A queue for all of the projects that need their code re-generating.
	/// </summary>
	private static ConcurrentQueue<LocalProject> NeedGenerating { get; } = new();
	/// <summary>
	/// A dictionary containing all of the projects with file system watchers enabled.
	/// </summary>
	private static Dictionary<LocalProject, FileSystemWatcher> Watchers { get; } = new();

	/// <summary>
	/// Checks all projects that need generating or are missing their file system watchers.
	/// </summary>
	[EditorEvent.Frame]
	private static void MonitorProjects()
	{
		foreach ( var project in Utility.Projects.GetAll() )
		{
			if ( project.Package.PackageType != Package.Type.Gamemode )
				continue;

			// In the event the root path changes but the project stays around. Update the path.
			if ( Watchers.TryGetValue( project, out var foundWatcher ) )
			{
				if ( foundWatcher.Path != project.GetRootPath() )
					foundWatcher.Path = project.GetRootPath();

				continue;
			}

			RealTimeSince timeSinceLastChange = 0;
			void AddonFileChanged( object sender, FileSystemEventArgs e )
			{
				// Typically change events fire twice.
				if ( timeSinceLastChange < 0.1f )
					return;

				timeSinceLastChange = 0;
				// Queue instead of generating now because of S&box thread safety checks.
				NeedGenerating.Enqueue( project );
			}

			var watcher = new FileSystemWatcher( project.GetRootPath(), ".addon" )
			{
				EnableRaisingEvents = true,
				NotifyFilter = NotifyFilters.LastWrite
			};
			watcher.Changed += AddonFileChanged;
			Watchers.Add( project, watcher );

			// Likely we've just booted up, make sure we generate an up to date version.
			NeedGenerating.Enqueue( project );
		}

		// Generate code for any projects that are requesting it.
		while ( NeedGenerating.TryDequeue( out var project ) )
			_ = GenerateForAsync( project );
	}

	/// <summary>
	/// Removes any file system watchers that are no longer valid.
	/// </summary>
	[Event( "localaddons.changed" )]
	private static void CleanWatchers()
	{
		var stillExists = new List<LocalProject>();
		foreach ( var project in Utility.Projects.GetAll() )
			stillExists.Add( project );

		var projectsToRemove = new Stack<LocalProject>();
		foreach ( var (project, watcher) in Watchers.Where( pair => !stillExists.Contains( pair.Key ) ) )
			projectsToRemove.Push( project );

		while ( projectsToRemove.TryPop( out var project ) )
		{
			Watchers[project].Dispose();
			Watchers.Remove( project );
		}	
	}

	/// <summary>
	/// Generates a input action data class for a given project.
	/// </summary>
	/// <param name="project">The project to generate the class for.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	/// <exception cref="ArgumentException">Thrown when given a project that cannot contain input actions.</exception>
	private static async Task GenerateForAsync( LocalProject project )
	{
		if ( project.Package.PackageType != Package.Type.Gamemode )
			throw new ArgumentException( $"The package type {project.Package.PackageType} cannot have input actions", nameof( project ) );

		var notice = new GeneratingNotice( project );

		if ( !project.Config.TryGetMeta<JsonElement>( "InputSettings", out var element ) )
		{
			notice.Error = GenerationError.NoInputSettings;
			return;
		}

		if ( !element.TryGetProperty( "Actions", out var actionsElement ) )
		{
			notice.Error = GenerationError.NoInputSettings;
			return;
		}

		if ( actionsElement.ValueKind != JsonValueKind.Array )
		{
			notice.Error = GenerationError.NoInputSettings;
			return;
		}

		notice.CurrentStage = GenerationStage.ParsingActions;
		var actions = new List<InputAction>();
		try
		{
			foreach ( var arrayElement in actionsElement.EnumerateArray() )
			{
				if ( arrayElement.Deserialize<InputAction>( JsonOptions ) is not InputAction action )
					continue;

				actions.Add( action );
			}
		}
		catch ( Exception )
		{
			notice.Error = GenerationError.ParseActionsFailed;
			return;
		}

		notice.CurrentStage = GenerationStage.FindingRootNamespace;
		var rootNamespace = "Sandbox";
		if ( !project.Config.TryGetMeta<CompilerSettings>( "Compiler", out var compilerSettings ) && compilerSettings is not null )
			rootNamespace = compilerSettings.RootNamespace;

		var outputDirectory = Path.Combine( project.GetCodePath(), "Generated" );
		var outputPath = Path.Combine( outputDirectory, "InputActions.generated.cs" );
		notice.CurrentStage = GenerationStage.OpeningGeneratedFile;

		Stream stream;
		try
		{
			if ( !Directory.Exists( outputDirectory ) )
				Directory.CreateDirectory( outputDirectory );

			stream = File.Open( outputPath, FileMode.Create );
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Failed to open {outputPath} for writing" );
			return;
		}

		using var writer = new IndentedTextWriter( new StreamWriter( stream ), "\t" );
		notice.CurrentStage = GenerationStage.GeneratingCode;

		await writer.WriteLineAsync( "// <auto-generated/>" );
		await writer.WriteLineAsync();

		// Don't need the using statement if we're under the namespace.
		if ( rootNamespace != "Sandbox" && !rootNamespace.StartsWith( "Sandbox." ) )
		{
			await writer.WriteLineAsync( "using Sandbox;" );
			await writer.WriteLineAsync();
		}

		await writer.WriteLineAsync( $"namespace {rootNamespace};" );
		await writer.WriteLineAsync();

		// Generate InputActions class.
		await writer.WriteLineAsync( "/// <summary>" );
		await writer.WriteLineAsync( $"/// Contains all input actions used in {project.Package.Title}." );
		await writer.WriteLineAsync( "/// </summary>" );
		await writer.WriteLineAsync( "public static class InputActions" );
		await writer.WriteLineAsync( '{' );
		writer.Indent++;

		// Generate each action data.
		foreach ( var action in actions )
		{
			var propertyName = action.Name;
			if ( propertyName.Length == 0 )
				continue;

			if ( char.IsDigit( propertyName[0] ) )
				propertyName = '_' + propertyName;

			await writer.WriteAsync( $"public static InputActionData {propertyName} {{ get; }} = new" );
			await writer.WriteLineAsync( $"( \"{action.Name}\", \"{action.GroupName}\", \"{action.KeyboardCode}\", {nameof( Gamepad )}.{nameof( Gamepad.Code )}.{action.GamepadCode} );" );
		}

		writer.Indent--;
		await writer.WriteLineAsync( '}' );
		await writer.WriteLineAsync();

		// Generate InputActionData struct, not using Sandbox.InputAction here because we want an implicit converter to the input name.
		await writer.WriteLineAsync( "public readonly struct InputActionData" );
		await writer.WriteLineAsync( '{' );
		writer.Indent++;

		// Properties.
		await writer.WriteLineAsync( "public string Name { get; }" );
		await writer.WriteLineAsync( "public string GroupName { get; }" );
		await writer.WriteLineAsync( "public string KeyboardCode { get; }" );
		await writer.WriteLineAsync( "public Gamepad.Code GamepadCode { get; }" );
		await writer.WriteLineAsync();

		// Constructor.
		await writer.WriteLineAsync( "public InputActionData( string name, string groupName, string keyboardCode, Gamepad.Code gamepadCode )" );
		await writer.WriteLineAsync( '{' );
		writer.Indent++;

		await writer.WriteLineAsync( "Name = name;" );
		await writer.WriteLineAsync( "GroupName = groupName;" );
		await writer.WriteLineAsync( "KeyboardCode = keyboardCode;" );
		await writer.WriteLineAsync( "GamepadCode = gamepadCode;" );

		writer.Indent--;
		await writer.WriteLineAsync( '}' );
		await writer.WriteLineAsync();

		// Implicit converter.
		await writer.WriteLineAsync( "public static implicit operator string( in InputActionData data ) => data.Name;" );

		writer.Indent--;
		await writer.WriteLineAsync( '}' );

		// Cleanup.
		writer.Close();
		notice.CurrentStage = GenerationStage.Finished;
	}
}

/// <summary>
/// A <see cref="JsonConverter"/> for the <see cref="Gamepad.Code"/> enum.
/// </summary>
internal sealed class CodeConverter : JsonConverter<Gamepad.Code>
{
	/// <inheritdoc/>
	public override Gamepad.Code Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		return Enum.Parse<Gamepad.Code>( reader.GetString()! );
	}

	/// <inheritdoc/>
	public override void Write( Utf8JsonWriter writer, Gamepad.Code value, JsonSerializerOptions options )
	{
		writer.WriteStringValue( value.ToString() );
	}
}
