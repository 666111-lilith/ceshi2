using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Palworks.Editor
{
    // This editor-only migration retains Unity GUIDs. No scene objects are rebuilt.
    public static class AssetOrganization
    {
        const string OldRoot = "Assets/Palworks";
        const string ReportRoot = "output/scene-review";
        const string ManifestPath = ReportRoot + "/asset-organization.json";
        [Serializable] public sealed class Entry { public string before, after, guid, sourceHash; }
        [Serializable] public sealed class Manifest { public string created; public Entry[] entries; public string[] rewritten; }
        sealed class Rewrite { public string beforePath, afterPath, before, after; }
        static readonly KeyValuePair<string,string>[] Prefixes = new[]
        {
            Pair("Assets/UniversalRenderPipelineGlobalSettings.asset", "Assets/Settings/Rendering/UniversalRenderPipelineGlobalSettings.asset"),
            Pair("Assets/Palworks/Character/Data", "Assets/Animations/Jenn/Data"),
            Pair("Assets/Palworks/Character/Prefabs", "Assets/Prefabs/Characters"),
            Pair("Assets/Palworks/Character/Runtime", "Assets/Scripts/Runtime/Character"),
            Pair("Assets/Palworks/Character/Editor", "Assets/Scripts/Editor/Character"),
            Pair("Assets/Palworks/Character/Shaders", "Assets/Shaders/Character"),
            Pair("Assets/Palworks/Environment/Shaders", "Assets/Shaders/Environment"),
            Pair("Assets/Palworks/Performance/Art", "Assets/Art/Stage"),
            Pair("Assets/Palworks/Performance/Runtime", "Assets/Scripts/Runtime/Performance"),
            Pair("Assets/Palworks/Performance/Editor", "Assets/Scripts/Editor/Performance"),
            Pair("Assets/Palworks/Performance/Shaders", "Assets/Shaders/Stage"),
            Pair("Assets/Palworks/Projection/Textures", "Assets/Settings/Rendering/RenderTextures"),
            Pair("Assets/Palworks/Projection/Materials", "Assets/Art/Stage/Projection/Materials"),
            Pair("Assets/Palworks/Projection/Meshes", "Assets/Art/Stage/Projection/Meshes"),
            Pair("Assets/Palworks/Projection/Runtime", "Assets/Scripts/Runtime/Projection"),
            Pair("Assets/Palworks/Projection/Editor", "Assets/Scripts/Editor/Projection"),
            Pair("Assets/Palworks/Projection/Shaders", "Assets/Shaders/Projection"),
            Pair("Assets/Palworks/Scene/Rendering/PaperDepthOfField.shader", "Assets/Shaders/PostProcessing/PaperDepthOfField.shader"),
            Pair("Assets/Palworks/Scene/Rendering", "Assets/Scripts/Runtime/Rendering"),
            Pair("Assets/Palworks/Scene/Runtime/CardObstacleCollider.cs", "Assets/Scripts/Runtime/Physics/CardObstacleCollider.cs"),
            Pair("Assets/Palworks/Scene/Runtime", "Assets/Scripts/Runtime/Camera"),
            Pair("Assets/Palworks/Scene/Editor", "Assets/Scripts/Editor/Scene"),
            Pair("Assets/Palworks/Scene/RoomGuide.cs", "Assets/Scripts/Runtime/Scene/RoomGuide.cs"),
            Pair("Assets/Palworks/Scene/Scene_Notes.txt", "Assets/Documentation/SceneNotes.txt"),
            Pair("Assets/Palworks/Character", "Assets/Art/Characters/Jenn"),
            Pair("Assets/Palworks/Environment", "Assets/Art/Environment"),
            Pair("Assets/Palworks/Performance", "Assets/Art/Stage"),
            Pair("Assets/Palworks/Projection", "Assets/Art/Stage/Projection"),
            Pair("Assets/Palworks/Scene", "Assets/Documentation/Scene")
        }.OrderByDescending(p=>p.Key.Length).ToArray();
        static KeyValuePair<string,string> Pair(string a,string b)=>new KeyValuePair<string,string>(a,b);
        static string Normalize(string path)=>path.Replace('\\','/');
        static string Destination(string path)
        {
            path=Normalize(path);
            foreach(var prefix in Prefixes)
                if(path==prefix.Key || path.StartsWith(prefix.Key+"/",StringComparison.Ordinal))
                    return prefix.Value+path.Substring(prefix.Key.Length);
            if(path.StartsWith(OldRoot+"/",StringComparison.Ordinal))
                throw new InvalidOperationException("Unclassified asset: "+path);
            return path;
        }
        static string Hash(string path)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
        }
        static bool Provenance(string path)=>Path.GetFileName(path).EndsWith("Sources.json",StringComparison.OrdinalIgnoreCase);
        static Manifest MakePlan()
        {
            if(!Directory.Exists(OldRoot))throw new InvalidOperationException("Assets are already organized. Use Validate instead.");
            var entries=Directory.GetFiles(OldRoot,"*",SearchOption.AllDirectories)
                .Concat(File.Exists("Assets/UniversalRenderPipelineGlobalSettings.asset") ? new[]{"Assets/UniversalRenderPipelineGlobalSettings.asset"} : Array.Empty<string>())
                .Where(p=>!p.EndsWith(".meta",StringComparison.OrdinalIgnoreCase))
                .Select(Normalize).OrderBy(p=>p,StringComparer.Ordinal)
                .Select(p=>new Entry {before=p,after=Destination(p),guid=AssetDatabase.AssetPathToGUID(p),sourceHash=Provenance(p)?Hash(p):null}).ToArray();
            if(entries.Any(e=>string.IsNullOrEmpty(e.guid)))throw new InvalidOperationException("Import all assets before organizing them.");
            if(entries.Select(e=>e.after).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=entries.Length)
                throw new InvalidOperationException("Asset destinations collide.");
            foreach(var entry in entries)
            {
                SafeAssetPath(entry.before);SafeAssetPath(entry.after);
                if(File.Exists(entry.after) || Directory.Exists(entry.after))throw new IOException("Destination already exists: "+entry.after);
            }
            return new Manifest {created=DateTime.Now.ToString("s"),entries=entries};
        }
        static void SafeAssetPath(string path)
        {
            string assets=Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string resolved=Path.GetFullPath(path);
            if(!resolved.StartsWith(assets,StringComparison.OrdinalIgnoreCase))throw new IOException("Path is outside Assets: "+path);
        }
        public static void Plan()
        {
            var manifest=MakePlan();Directory.CreateDirectory(ReportRoot);
            File.WriteAllText(ReportRoot+"/asset-organization-plan.json",JsonUtility.ToJson(manifest,true));
        }
        static void EnsureFolder(string path)
        {
            path=Normalize(path);SafeAssetPath(path+"/placeholder");
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=Normalize(Path.GetDirectoryName(path));
            if(parent!="Assets")EnsureFolder(parent);
            string guid=AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
            if(string.IsNullOrEmpty(guid))throw new IOException("Cannot create folder "+path);
        }
        static string ReplacePaths(string text)
        {
            // Preserve source names / shader identifiers; rewrite only project asset paths.
            foreach(var prefix in Prefixes)text=text.Replace(prefix.Key,prefix.Value);
            return text;
        }
        static string RewriteText(string path,string text)
        {
            if(path.EndsWith(".cs",StringComparison.OrdinalIgnoreCase))
            {
                // Expand const Root + "/Data/..." before the root itself is split among categories.
                for(int pass=0;pass<4;pass++)
                {
                    var constants=Regex.Matches(text,@"\bconst\s+string\s+(\w+)\s*=\s*""([^""\r\n]*)""")
                        .Cast<Match>().Where(m=>m.Groups[2].Value.StartsWith(OldRoot,StringComparison.Ordinal)).ToArray();
                    bool changed=false;
                    foreach(var constant in constants)
                    {
                        string name=constant.Groups[1].Value,value=constant.Groups[2].Value;
                        string pattern=@"\b"+Regex.Escape(name)+@"\s*\+\s*""([^""\r\n]*)""";
                        string updated=Regex.Replace(text,pattern,m=>"\""+value+m.Groups[1].Value+"\"");
                        changed|=updated!=text;text=updated;
                    }
                    if(!changed)break;
                }
                if(Path.GetFileName(path)=="MovementUpgrade.cs")
                    text=Regex.Replace(text,@"string root=Path\.GetDirectoryName\(Path\.GetDirectoryName\(AssetDatabase\.GetAssetPath\(player\.puppet\.animationJson\)\)\)\.Replace\([^;]+;",
                        "string root=\"Assets/Art/Characters/Jenn\";");
                return ReplacePaths(text);
            }
            text=ReplacePaths(text);
            if(Path.GetFileName(path)=="export_character.py")
                text=text.Replace("DATA = DEST / 'Data'","DATA = PROJECT / 'Assets/Animations/Jenn/Data'");
            if(Path.GetFileName(path)=="extend_movement.py")
            {
                text=text.Replace("ROOT = PROJECT / 'Assets/Characters/Jenn'","ROOT = PROJECT / 'Assets/Art/Characters/Jenn'");
                text=text.Replace("ROOT / 'Data/JennAnimations.json'","PROJECT / 'Assets/Animations/Jenn/Data/JennAnimations.json'");
                text=text.Replace("ROOT / 'Data/MovementSources.json'","PROJECT / 'Assets/Animations/Jenn/Data/MovementSources.json'");
            }
            return text;
        }
        static List<Rewrite> BuildRewrites(Manifest manifest)
        {
            var map=manifest.entries.ToDictionary(e=>e.before,e=>e.after,StringComparer.OrdinalIgnoreCase);
            var paths=Directory.GetFiles("Assets","*",SearchOption.AllDirectories)
                .Where(p=>p.EndsWith(".cs",StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt",StringComparison.OrdinalIgnoreCase) || p.EndsWith(".md",StringComparison.OrdinalIgnoreCase)).ToList();
            if(Directory.Exists("Tools"))paths.AddRange(Directory.GetFiles("Tools","*.py",SearchOption.AllDirectories));
            var rewrites=new List<Rewrite>();
            foreach(var item in paths)
            {
                string path=Normalize(item);
                // Keep this one-shot migration's source paths intact for validation and recovery.
                if(Path.GetFileName(path)=="AssetOrganization.cs")continue;
                string text=File.ReadAllText(path),updated=RewriteText(path,text);
                if(updated==text)continue;
                rewrites.Add(new Rewrite {beforePath=path,afterPath=map.TryGetValue(path,out var moved)?moved:path,before=text,after=updated});
            }
            return rewrites;
        }
        public static void Apply()
        {
            if(EditorApplication.isPlaying || EditorApplication.isCompiling)throw new InvalidOperationException("Organize assets in Edit mode after compilation.");
            var manifest=MakePlan();var rewrites=BuildRewrites(manifest);
            manifest.rewritten=rewrites.Select(r=>r.afterPath).ToArray();
            Directory.CreateDirectory(ReportRoot);
            File.WriteAllText(ReportRoot+"/asset-organization-plan.json",JsonUtility.ToJson(manifest,true));
            string backupRoot=ReportRoot+"/asset-organization-backup-"+DateTime.Now.ToString("yyyyMMddHHmmss");
            foreach(var rewrite in rewrites)
            {
                string backup=backupRoot+"/"+rewrite.beforePath;Directory.CreateDirectory(Path.GetDirectoryName(backup));File.WriteAllText(backup,rewrite.before,new UTF8Encoding(false));
            }
            var scene=SceneManager.GetActiveScene();
            if(scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                EnsureFolder("Assets/Scenes/Backups");
                if(!EditorSceneManager.SaveScene(scene,"Assets/Scenes/Backups/BeforeAssetOrganization"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity",true))
                    throw new IOException("Cannot preserve current scene before asset organization.");
                EditorSceneManager.SaveScene(scene);
            }
            var completed=new List<Entry>();
            EditorApplication.LockReloadAssemblies();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(var entry in manifest.entries)
                {
                    EnsureFolder(Normalize(Path.GetDirectoryName(entry.after)));
                    string error=AssetDatabase.MoveAsset(entry.before,entry.after);
                    if(!string.IsNullOrEmpty(error))throw new IOException(error);
                    completed.Add(entry);
                }
                foreach(var rewrite in rewrites)File.WriteAllText(rewrite.afterPath,rewrite.after,new UTF8Encoding(false));
                foreach(var entry in manifest.entries)
                    if(Provenance(entry.after) && Hash(entry.after)!=entry.sourceHash)throw new IOException("Source provenance was modified: "+entry.after);
                File.WriteAllText(ManifestPath,JsonUtility.ToJson(manifest,true));
            }
            catch
            {
                foreach(var rewrite in rewrites)
                    if(File.Exists(rewrite.afterPath))File.WriteAllText(rewrite.afterPath,rewrite.before,new UTF8Encoding(false));
                for(int i=completed.Count-1;i>=0;i--)
                {
                    EnsureFolder(Normalize(Path.GetDirectoryName(completed[i].before)));
                    string error=AssetDatabase.MoveAsset(completed[i].after,completed[i].before);
                    if(!string.IsNullOrEmpty(error))Debug.LogError("Asset rollback: "+error);
                }
                throw;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();EditorApplication.UnlockReloadAssemblies();
            }
            RemoveEmptyLegacyFolders();
            AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Validate();
        }
        static void RemoveEmptyLegacyFolders()
        {
            if(!Directory.Exists(OldRoot))return;
            foreach(var folder in Directory.GetDirectories(OldRoot,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length).Concat(new[]{OldRoot}))
            {
                string path=Normalize(folder);SafeAssetPath(path+"/placeholder");
                if(Directory.GetFiles(path).Any(p=>!p.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)) || Directory.GetDirectories(path).Length!=0)continue;
                if(!AssetDatabase.DeleteAsset(path))Debug.LogWarning("Empty legacy folder remains: "+path);
            }
        }
        public static void Validate()
        {
            if(!File.Exists(ManifestPath))throw new FileNotFoundException("No completed asset organization manifest.",ManifestPath);
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            var issues=new List<string>();
            foreach(var entry in manifest.entries)
            {
                if(!File.Exists(entry.after))issues.Add("Missing "+entry.after);
                else if(AssetDatabase.AssetPathToGUID(entry.after)!=entry.guid)issues.Add("GUID changed "+entry.after);
                if(File.Exists(entry.before))issues.Add("Old asset remains "+entry.before);
                if(Provenance(entry.after) && File.Exists(entry.after) && Hash(entry.after)!=entry.sourceHash)issues.Add("Provenance changed "+entry.after);
            }
            if(Directory.Exists(OldRoot))issues.Add("Legacy parent folder still exists");
            var scene=SceneManager.GetActiveScene();
            if(scene.IsValid())foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)issues.Add("Missing script "+t.name);
            Directory.CreateDirectory(ReportRoot);
            File.WriteAllText(ReportRoot+"/asset-organization-check.txt","Result="+(issues.Count==0?"PASS":"FAIL")+"\nAssets="+manifest.entries.Length+"\nGUIDsPreserved="+(manifest.entries.Length-issues.Count)+"\nRewrittenFiles="+(manifest.rewritten?.Length??0)+"\nProvenance=ByteIdentical\n"+string.Join("\n",issues));
            if(issues.Count>0)throw new InvalidOperationException(string.Join("\n",issues));
        }
    }
}
