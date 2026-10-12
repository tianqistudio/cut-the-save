using System.Security.Cryptography;
using CutTheSave.Core;
using NSec.Cryptography;

var options = ParseArgs(args);
if (!options.TryGetValue("manifest", out var manifestPath)
    || !options.TryGetValue("signature", out var signaturePath)
    || (!options.ContainsKey("private-key-base64") && !options.ContainsKey("private-key-file")))
{
    Console.Error.WriteLine("Usage: CutTheSave.Signing --manifest <manifest.json> --signature <manifest.json.sig> [--public-key <public-key.txt>] (--private-key-base64 <base64> | --private-key-file <raw-key-file>)");
    return 2;
}

try
{
    var manifestBytes = File.ReadAllBytes(Path.GetFullPath(manifestPath));
    _ = UpdateManifestCodec.Parse(manifestBytes);
    var privateKeyBytes = ReadPrivateKey(options);
    if (privateKeyBytes.Length != SignatureAlgorithm.Ed25519.PrivateKeySize)
        throw new ArgumentException($"An Ed25519 raw private key must be {SignatureAlgorithm.Ed25519.PrivateKeySize} bytes.");

    using var key = Key.Import(SignatureAlgorithm.Ed25519, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
    var signature = SignatureAlgorithm.Ed25519.Sign(key, manifestBytes);
    WriteBytesAtomically(Path.GetFullPath(signaturePath), signature);

    if (options.TryGetValue("public-key", out var publicKeyPath))
    {
        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        WriteTextAtomically(Path.GetFullPath(publicKeyPath), Convert.ToBase64String(publicKey) + Environment.NewLine);
    }

    Console.WriteLine($"Signed manifest: {Path.GetFullPath(manifestPath)}");
    Console.WriteLine($"Signature: {Path.GetFullPath(signaturePath)}");
    if (options.ContainsKey("public-key"))
        Console.WriteLine($"Public key: {Path.GetFullPath(options["public-key"])}");
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or FormatException or IOException or UnauthorizedAccessException or CryptographicException)
{
    Console.Error.WriteLine($"Signing failed: {exception.Message}");
    return 3;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index + 1 < args.Length; index += 2)
    {
        if (!args[index].StartsWith("--", StringComparison.Ordinal))
            continue;
        result[args[index][2..]] = args[index + 1];
    }
    return result;
}

static byte[] ReadPrivateKey(IReadOnlyDictionary<string, string> options)
{
    if (options.TryGetValue("private-key-file", out var keyFile))
        return File.ReadAllBytes(Path.GetFullPath(keyFile));

    try
    {
        return Convert.FromBase64String(options["private-key-base64"]);
    }
    catch (FormatException exception)
    {
        throw new ArgumentException("The private key is not valid Base64.", exception);
    }
}

static void WriteBytesAtomically(string path, byte[] bytes)
{
    var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("The output path has no directory.");
    Directory.CreateDirectory(directory);
    var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
    try
    {
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporary))
            File.Delete(temporary);
    }
}

static void WriteTextAtomically(string path, string text) => WriteBytesAtomically(path, System.Text.Encoding.UTF8.GetBytes(text));
