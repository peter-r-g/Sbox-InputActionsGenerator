﻿using Editor;
using Sandbox;
using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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

	private static ConcurrentQueue<LocalProject> NeedGenerating { get; } = new();

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

			void AddonFileChanged( object sender, FileSystemEventArgs e )
			{
				// Queue instead of generating because of S&box thread safety checks.
				NeedGenerating.Enqueue( project );
			}

			var watcher = new FileSystemWatcher( project.GetRootPath(), ".addon" )
			{
				EnableRaisingEvents = true,
				NotifyFilter = NotifyFilters.LastWrite
			};
			watcher.Changed += AddonFileChanged;
			Watchers.Add( project, watcher );
		}

		// Generate code for any projects that are requesting it.
		while ( NeedGenerating.TryDequeue( out var project ) )
			GenerateFor( project );
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
	/// <exception cref="ArgumentException">Thrown when given a project that cannot contain input actions.</exception>
	private static void GenerateFor( LocalProject project )
	{
		if ( project.Package.PackageType != Package.Type.Gamemode )
			throw new ArgumentException( $"The package type {project.Package.PackageType} cannot have input actions", nameof( project ) );

		using var progress = Progress.Start( $"Generating input action file for {project.Package.Title}" );

		Progress.Update( "Looking for input actions...", 5, 100 );
		if ( !project.Config.TryGetMeta<JsonElement>( "InputSettings", out var element ) )
		{
			Log.Error( "Failed to find input settings in .addon file" );
			return;
		}

		if ( !element.TryGetProperty( "Actions", out var actionsElement ) )
		{
			Log.Error( "Failed to find input settings in .addon file" );
			return;
		}

		if ( actionsElement.ValueKind != JsonValueKind.Array )
		{
			Log.Error( "Failed to find input settings in .addon file" );
			return;
		}

		Progress.Update( "Parsing input actions...", 30, 100 );
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
		catch ( Exception e )
		{
			Log.Error( e, "Failed to parse input actions in .addon file" );
		}

		Progress.Update( "Finding root namespace...", 45, 100 );
		var rootNamespace = "Sandbox";
		if ( !project.Config.TryGetMeta<CompilerSettings>( "Compiler", out var compilerSettings ) && compilerSettings is not null )
			rootNamespace = compilerSettings.RootNamespace;

		var outputPath = Path.Combine( project.GetCodePath(), "InputActions.cs" );
		Progress.Update( $"Opening ${outputPath} for writing...", 50, 100 );

		Stream stream;
		try
		{
			stream = File.Open( outputPath, FileMode.Create );
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Failed to open {outputPath} for writing" );
			return;
		}

		using var writer = new IndentedTextWriter( new StreamWriter( stream ), "\t" );

		Progress.Update( "Generating code...", 60, 100 );
		writer.WriteLine( "// <auto-generated/>" );
		writer.WriteLine();

		// Don't need the using statement if we're under the namespace.
		if ( rootNamespace != "Sandbox" && !rootNamespace.StartsWith( "Sandbox." ) )
		{
			writer.WriteLine( "using Sandbox;" );
			writer.WriteLine();
		}

		writer.WriteLine( $"namespace {rootNamespace};" );
		writer.WriteLine();

		// Generate InputActions class.
		writer.WriteLine( "/// <summary>" );
		writer.WriteLine( $"/// Contains all input actions used in {project.Package.Title}." );
		writer.WriteLine( "/// </summary>" );
		writer.WriteLine( "public static class InputActions" );
		writer.WriteLine( '{' );
		writer.Indent++;

		// Generate each action data.
		foreach ( var action in actions )
		{
			writer.Write( $"public static InputActionData {action.Name} {{ get; }} = new" );
			writer.WriteLine( $"( \"{action.Name}\", \"{action.GroupName}\", \"{action.KeyboardCode}\", {nameof( Gamepad )}.{nameof( Gamepad.Code )}.{action.GamepadCode} );" );
		}

		writer.Indent--;
		writer.WriteLine( '}' );
		writer.WriteLine();

		// Generate InputActionData struct, not using Sandbox.InputAction here because we want an implicit converter to the input name.
		writer.WriteLine( "public readonly struct InputActionData" );
		writer.WriteLine( '{' );
		writer.Indent++;

		// Properties.
		writer.WriteLine( "public string Name { get; }" );
		writer.WriteLine( "public string GroupName { get; }" );
		writer.WriteLine( "public string KeyboardCode { get; }" );
		writer.WriteLine( "public Gamepad.Code GamepadCode { get; }" );
		writer.WriteLine();

		// Constructor.
		writer.WriteLine( "public InputActionData( string name, string groupName, string keyboardCode, Gamepad.Code gamepadCode )" );
		writer.WriteLine( '{' );
		writer.Indent++;

		writer.WriteLine( "Name = name;" );
		writer.WriteLine( "GroupName = groupName;" );
		writer.WriteLine( "KeyboardCode = keyboardCode;" );
		writer.WriteLine( "GamepadCode = gamepadCode;" );

		writer.Indent--;
		writer.WriteLine( '}' );
		writer.WriteLine();

		// Implicit converter.
		writer.WriteLine( "public static implicit operator string( in InputActionData data ) => data.Name;" );

		writer.Indent--;
		writer.WriteLine( '}' );

		// Cleanup.
		writer.Close();
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
