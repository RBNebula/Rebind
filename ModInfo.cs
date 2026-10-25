namespace Rebind;

internal static class ModInfo
{
    public const string PLUGIN_GUID = "com.rebind";
    public const string PLUGIN_NAME = "Rebind";
    public const string PLUGIN_VERSION = "1.3.1";
    public const string ASSEMBLY_VERSION = "1.3.1.0";
    public const string FILE_VERSION = "1.3.1.0";
    public const string INFORMATIONAL_VERSION = "1.3.1";

    public const string HARMONY_ID = PLUGIN_GUID;
    public const string LOG_PREFIX = "[Rebind]";
    public const string DISPLAY_NAME_SHORT = "Rebind";
    public const string DISPLAY_NAME_LONG = "Rebind Keybind Manager";

    // Common config section names.
    public const string CONFIG_SECTION_DEBUG = "Debug";
    public const string CONFIG_SECTION_UI = "UI";
    public const string CONFIG_SECTION_KEYBINDS = "Keybinds";

    // Optional versioning markers for your own settings/save migrations.
    public const int SETTINGS_SCHEMA_VERSION = 1;
    public const int REGISTRY_SCHEMA_VERSION = 1;

    // Optional informational game targeting metadata.
    public const string TARGET_GAME = "MineMogul";
    public const string TARGET_GAME_VERSION = "unknown";
}
