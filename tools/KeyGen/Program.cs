using System.Security.Cryptography;

using var rsa = RSA.Create(2048);

var privateKey = rsa.ExportRSAPrivateKeyPem();
var publicKey = rsa.ExportSubjectPublicKeyInfoPem();

Console.WriteLine("PRIVATE KEY:");
Console.WriteLine(privateKey);
Console.WriteLine();
Console.WriteLine("PUBLIC KEY:");
Console.WriteLine(publicKey);