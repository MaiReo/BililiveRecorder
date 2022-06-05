using System;
using System.IO.Abstractions;

namespace k8s;

/// <summary>
/// Peek from https://github.com/kubernetes-client/csharp/blob/master/src/KubernetesClient/KubernetesClientConfiguration.InCluster.cs
/// </summary>
public partial class KubernetesClientConfiguration
{
    private static readonly string ServiceAccountPath =
        FS!.Path.Combine(new string[]
        {
                $"{FS.Path.DirectorySeparatorChar}var", "run", "secrets", "kubernetes.io", "serviceaccount",
        });

    internal const string ServiceAccountTokenKeyFileName = "token";
    internal const string ServiceAccountRootCAKeyFileName = "ca.crt";
    internal const string ServiceAccountNamespaceFileName = "namespace";

    public static bool IsInCluster()
    {
        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port))
        {
            return false;
        }

        var tokenPath = FS.Path.Combine(ServiceAccountPath, ServiceAccountTokenKeyFileName);
        if (!FS.File.Exists(tokenPath))
        {
            return false;
        }

        var certPath = FS.Path.Combine(ServiceAccountPath, ServiceAccountRootCAKeyFileName);
        return FS.File.Exists(certPath);
    }

    private readonly static FileSystem FS = new();
}