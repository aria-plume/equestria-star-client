using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace EquestriaStar.Internal.MapBaking;

/// <summary>只打包静态地图及其资源依赖，不收集脚本和业务场景。</summary>
public static class StaticMapPackager
{
    public sealed record PackResult(bool Ok, string Error, int ResourceCount, long Bytes, string Sha256);

    public static PackResult Build(string scenePath, string metadataPath, string outputPath)
    {
        try
        {
            var resources = CollectDependencies(scenePath);
            resources.Add(scenePath);
            resources.Add(metadataPath);
            var absoluteOutput = ProjectSettings.GlobalizePath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
            if (File.Exists(absoluteOutput)) File.Delete(absoluteOutput);
            var packer = new PckPacker();
            var error = packer.PckStart(absoluteOutput);
            if (error != Error.Ok) return new(false, $"PCK 初始化失败：{error}", 0, 0, "");

            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (var resource in resources)
            {
                if (!IsAllowed(resource))
                    return new(false, $"PCK 依赖包含禁止类型：{resource}", 0, 0, "");
                AddIfPresent(packer, added, resource);
                AddImportArtifacts(packer, added, resource);
            }
            error = packer.Flush(true);
            if (error != Error.Ok) return new(false, $"PCK 写入失败：{error}", added.Count, 0, "");
            var info = new FileInfo(absoluteOutput);
            using var stream = File.OpenRead(absoluteOutput);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
            return new(true, "", added.Count, info.Length, hash);
        }
        catch (Exception exception)
        {
            return new(false, exception.Message, 0, 0, "");
        }
    }

    private static HashSet<string> CollectDependencies(string entry)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(entry);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var raw in ResourceLoader.GetDependencies(current))
            {
                var dependency = NormalizeDependency(raw);
                if (dependency.StartsWith("res://", StringComparison.Ordinal) && result.Add(dependency)) pending.Enqueue(dependency);
            }
        }
        return result;
    }

    private static string NormalizeDependency(string value)
    {
        var marker = value.LastIndexOf("::", StringComparison.Ordinal);
        return marker >= 0 ? value[(marker + 2)..] : value;
    }

    private static bool IsAllowed(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".tscn" or ".tres" or ".res" or ".glb" or ".gltf" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg" or ".gdshader" or ".json";
    }

    private static void AddIfPresent(PckPacker packer, HashSet<string> added, string virtualPath)
    {
        if (!added.Add(virtualPath)) return;
        var source = ProjectSettings.GlobalizePath(virtualPath);
        if (!File.Exists(source)) throw new FileNotFoundException("静态地图依赖不存在", virtualPath);
        var error = packer.AddFile(virtualPath, source);
        if (error != Error.Ok) throw new InvalidOperationException($"添加 PCK 文件失败 {virtualPath}：{error}");
    }

    private static void AddImportArtifacts(PckPacker packer, HashSet<string> added, string resourcePath)
    {
        var importVirtual = resourcePath + ".import";
        var importAbsolute = ProjectSettings.GlobalizePath(importVirtual);
        if (!File.Exists(importAbsolute)) return;
        AddIfPresent(packer, added, importVirtual);
        var config = new ConfigFile();
        if (config.Load(importAbsolute) != Error.Ok) return;
        foreach (var key in new[] { "path", "path.s3tc", "path.etc2", "path.bptc" })
        {
            if (!config.HasSectionKey("remap", key)) continue;
            var imported = config.GetValue("remap", key).AsString();
            if (imported.StartsWith("res://", StringComparison.Ordinal) && File.Exists(ProjectSettings.GlobalizePath(imported)))
                AddIfPresent(packer, added, imported);
        }
    }
}
