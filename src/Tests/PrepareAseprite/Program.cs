using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
// current all permissions value is 31, but in case any new permissions are added,
// this makes sure they will be covered.
const int ACCESS_VAL = int.MaxValue; 

string? config_dir = Environment.GetEnvironmentVariable("ASEPRITE_USER_FOLDER");

if (config_dir == null)
    throw new ArgumentException("ASEPRITE_USER_FOLDER environment variable is not set, unable to give script permissions if aseprite config dir is unknown.");

// location of debugger source directory should be stored in the ASEPRITE_DEB_TEST_SCRIPT
string? src_dir = Path.Join(config_dir, Path.Join("extensions", "!AsepriteDebugger"));

string aseprite_init_file = Path.Join(config_dir, "aseprite.ini");

Console.WriteLine($"Using following ini file: {aseprite_init_file}");

new FileInfo(aseprite_init_file).Directory?.Create();

File.WriteAllText(aseprite_init_file, "[script_access]\n");

foreach (string script in Directory.EnumerateFiles(src_dir, "*.lua", SearchOption.AllDirectories))
{
    Console.WriteLine($"Found following script file:\t\"{script}\"");

    // compute sha1 string form script path.

    SHA1 sha = SHA1.Create();
    string script_key = Convert.ToHexString(sha.ComputeHash(Encoding.Default.GetBytes(script)));
    // aseprite uses lowercase for hex string and c# uses upper case,
    // and since the key is used as a string, the casings need to match up.
    script_key = script_key.ToLower();

    Console.WriteLine($"Generated script key:\t{script_key}");

    File.AppendAllText(aseprite_init_file, $"{script_key} = {ACCESS_VAL}\n");
}