// Copyright 2021 by Hextant Studios. https://HextantStudios.com
// This work is licensed under CC BY 4.0. http://creativecommons.org/licenses/by/4.0/
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Hextant
{
    // Base class for project/users settings. Use the [Settings] attribute to
    // specify its usage, display path, and filename.
    // * Derived classes *must* be placed in a file with the same name as the class.
    // * Settings are stored in Assets/Settings/ folder.
    // * The user settings folder Assets/Settings/Editor/User/ *must* be
    //   excluded from source control.
    // * User settings will be placed in a subdirectory named the same as
    //   the current project folder so that shallow cloning (symbolic links to
    //   the Assets/ folder) can be used when testing multiplayer games.
    // See: https://HextantStudios.com/unity-custom-settings/

    // This whole thing might fuck up if some methods are called before init



    public abstract class Settings<T> : ScriptableObject where T : Settings<T>
    {
        // The singleton instance. (Not thread safe but fine for ScriptableObjects.)
        public static T instance => _instance != null ? _instance : Initialize();
        static T _instance;
        static bool _isDirty = false;
        static string filename;
        //static string path;
        static string projectpath;
        static string documentspath;
        static string resourcepath;

        protected static void InitFilenamePath()
        {
            filename = attribute.filename ?? typeof( T ).Name;
            projectpath = GetProjectSettingsPath() + filename + ".asset";
            resourcepath = GetResourcePath() + filename;

            if (attribute.usage == SettingsUsage.RuntimeUser)
                documentspath = GetDocumentsSettingsPath() + filename + ".json";
        }

        // Loads or creates the settings instance and stores it in _instance.
        protected static T Initialize()
        {
            // If the instance is already valid, return it. Needed if called from a
            // derived class that wishes to ensure the settings are initialized.
            if ( _instance != null ) return _instance;

            // Verify there was a [Settings] attribute.
            if ( attribute == null )
                throw new System.InvalidOperationException(
                    "[Settings] attribute missing for type: " + typeof( T ).Name );

            // Attempt to load the settings asset.
            InitFilenamePath();

            _instance = Resources.Load<T>( resourcepath );

#if !UNITY_EDITOR
            if ( attribute.usage == SettingsUsage.RuntimeUser && !TryLoadSettings())
                SaveSettings( ignoreDirtyFlag: true );
#endif

#if UNITY_EDITOR


            // Return the instance if it was the load was successful.
            if ( _instance != null ) return _instance;

            // Move settings if its path changed (type renamed or attribute changed)
            // while the editor was running. This must be done manually if the
            // change was made outside the editor.
            var instances = Resources.FindObjectsOfTypeAll<T>();
            if ( instances.Length > 0 )
            {
                var oldPath = AssetDatabase.GetAssetPath( instances[ 0 ] );
                var result = AssetDatabase.MoveAsset( oldPath, projectpath );
                if ( string.IsNullOrEmpty( result ) )
                    return _instance = instances[ 0 ];
                else
                {
                    Debug.LogWarning( $"'{result}'. Failed to move previous settings asset " +
                                      $"'{oldPath}' to '{projectpath}'. " +
                                      $"A new settings asset will be created.", _instance );
                }
            }
#endif
            // Create the settings instance if it was not loaded or found.
            if ( _instance != null ) return _instance;
            _instance = CreateInstance<T>();

#if UNITY_EDITOR
            // Verify the derived class is in a file with the same name.
            var script = MonoScript.FromScriptableObject( _instance );
            if ( script == null || script.name != typeof( T ).Name )
            {
                DestroyImmediate( _instance );
                _instance = null;
                throw new System.InvalidOperationException(
                    "Settings-derived class and filename must match: " +
                        typeof( T ).Name );
            }

            // Create a new settings instance if it was not found.
            // Create the directory as Unity does not do this itself.
            Directory.CreateDirectory( Path.Combine(
                Directory.GetCurrentDirectory(),
                Path.GetDirectoryName( projectpath ) ) );

            // Create the asset only in the editor.
            AssetDatabase.CreateAsset( _instance, projectpath );
#endif
            return _instance;
        }

        // Returns the full asset path to the settings file.
        public static string GetProjectSettingsPath()
        {
            return "Assets/Resources/" + GetResourcePath();
        }

        public static string GetResourcePath()
        {
            var path = "Settings/";

            switch( attribute.usage )
            {
                case SettingsUsage.RuntimeUser:
                    path += "RuntimeUser/"; break;
                case SettingsUsage.RuntimeProject:
                    path += "RuntimeProject/"; break;
#if UNITY_EDITOR
                case SettingsUsage.EditorProject:
                    path += "EditorProject/"; break;
                case SettingsUsage.EditorUser:
                    path += "EditorUser/"; break;
#endif
                default: throw new System.InvalidOperationException();
            }
            return path;

        }

        public static string GetDocumentsSettingsPath()
        {
            Debug.Assert( attribute.usage == SettingsUsage.RuntimeUser );

            return Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ) + '\\'
                + GetProjectFolderName() + '\\';
        }

        // The derived type's [Settings] attribute.
        public static SettingsAttribute attribute =>
            _attribute != null ? _attribute : _attribute =
                typeof( T ).GetCustomAttribute<SettingsAttribute>( true );
        static SettingsAttribute _attribute;

        // Called to validate settings changes.
        protected virtual void OnValidate() { }

        // Sets the specified setting to the desired value and marks the settings
        // so that it will be saved.
        protected void Set<S>( ref S setting, S value )
        {
            if ( EqualityComparer<S>.Default.Equals( setting, value ) ) return;
            setting = value;
            OnValidate();
            SetDirty();
        }

        // Marks the settings dirty so that it will be saved.
        protected new void SetDirty()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty( this );
#endif
            _isDirty = true;
        }

        // The directory name of the current project folder.
        static string GetProjectFolderName()
        {
            var path = Application.dataPath.Split( '/' );
            return path[ path.Length - 2 ];
        }

        /// <summary>
        /// Saves the current setting to disk, only for RuntimeUser. Skips it if the setting is not dirty (not set recently)
        /// </summary>
        /// <param name="ignoreDirtyFlag">Set this true to ignore the dirty flag check</param>
        public static void SaveSettings(bool ignoreDirtyFlag = false)
        {
            Debug.Assert( attribute.usage == SettingsUsage.RuntimeUser );

            if ( !ignoreDirtyFlag && !_isDirty )
                return;

            if ( !Directory.Exists( GetDocumentsSettingsPath() ) )
                Directory.CreateDirectory( GetDocumentsSettingsPath() );

            File.WriteAllText( documentspath, JsonUtility.ToJson( _instance, true ) );
            _isDirty = false;
        }

        /// <summary>
        /// Load the current setting from disk, only for RuntimeUser
        /// </summary>
        /// <returns>True if load goes successfully, false if errors are present</returns>
        public static bool TryLoadSettings()
        {
            Debug.Assert( attribute.usage == SettingsUsage.RuntimeUser );

            if( !File.Exists( documentspath ) )
            {
                Debug.Log( "File " + documentspath + " does not exist" );
                return false;
            }

            try
            {
                JsonUtility.FromJsonOverwrite( File.ReadAllText( documentspath ), _instance );
            }
            catch( Exception e )
            {
                Debug.LogWarning( e );
                return false;
            }

            return true;
        }


        // Base class for settings contained by a Settings<T> instance.
        [Serializable]
        public abstract class SubSettings
        {
            // Called when a setting is modified.
            protected virtual void OnValidate() { }

#if UNITY_EDITOR
            // Sets the specified setting to the desired value and marks the settings
            // instance so that it will be saved.
            protected void Set<S>( ref S setting, S value )
            {
                if ( EqualityComparer<S>.Default.Equals( setting, value ) ) return;
                setting = value;
                OnValidate();
                instance.SetDirty();
            }
#endif
        }
    }
}