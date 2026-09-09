using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace PointCursor
{
    internal static class RuntimeAssets
    {
        // Cheap length checks on startup; full hashes in build/diagnostics. A Git LFS
        // pointer has the wrong length and can never masquerade as an installed model.
        public static string Validate(string root, bool hashes)
        {
            try
            {
                string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string manifest = Path.Combine(basePath, "assets.tsv");
                if (!File.Exists(manifest)) return "Kokoro 文件清单缺失，请重新构建或解压完整发布包。";
                int count = 0;
                foreach (string line in File.ReadLines(manifest))
                {
                    if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] parts = line.Split('\t'); long size;
                    if (parts.Length != 3 || !Int64.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 0 || parts[1].Length != 64)
                        return "Kokoro 文件清单格式错误。";
                    string path = Path.GetFullPath(Path.Combine(basePath, parts[2]));
                    if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return "Kokoro 文件清单路径错误。";
                    if (!File.Exists(path) || new FileInfo(path).Length != size) return "Kokoro 文件缺失或不完整：" + parts[2] + "。请执行 git lfs pull 后重新构建。";
                    if (hashes) using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                    {
                        string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                        if (actual != parts[1]) return "Kokoro 文件校验失败：" + parts[2];
                    }
                    count++;
                }
                return count < 20 ? "Kokoro 文件清单不完整。" : null;
            }
            catch (IOException) { return "Kokoro 文件无法读取，请检查发布目录。"; }
            catch (UnauthorizedAccessException) { return "Kokoro 文件无法访问，请检查目录权限。"; }
            catch (ArgumentException) { return "Kokoro 文件清单路径错误。"; }
        }
    }
}
