#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditorInternal;
using CanvasTools;
using System.Runtime.CompilerServices;

public class AI_CodeAssistant : EditorWindow
{
    public const string GPT4O = "gpt-4o";
    public const string GPT4O_MINI = "gpt-4o-mini";
    public const string GPT35_TURBO = "gpt-3.5-turbo";
    public const string O1_PREVIEW = "o1-preview";
    public const string O1_PREVIEW_MINI = "o1-mini";
    public const string API_ENDPOINT = "https://api.openai.com/v1/chat/completions";
    public const string FILE_ENDPOINT = "https://api.openai.com/v1/files";
    public const string MODEL_ENDPOINT = "https://api.openai.com/v1/models";
    public const string DASHBOARD_COMPLETION = "https://platform.openai.com/chat-completions";
    public const string DASHBOARD_FINETUNING = "https://platform.openai.com/finetune";
    public const string DASHBOARD_DEV_APIKEYS = "https://platform.openai.com/api-keys";
    public const string CODEWRX_AI_DASHBOARD = "https://codewrx.ai";
    public const string CODEWRX_AI_EXTENSION = "https://github.com/codewrxai/ai-code-assistant-editor";
    public const string VSCODE_AI_EXTENSION = "https://marketplace.visualstudio.com/items?itemName=AndrewButson.vscode-openai";
    public const string DEFAULT_DEVELOPER_PERSONA = "You are a Developer/Programmer working in the game development industry. Your job is to design, develop, and maintain software applications and systems. You are responsible for writing code that is efficient, scalable, and easy to maintain. As a Developer/Programmer, you must be knowledgeable about various programming languages, frameworks, and tools. You must also be able to work collaboratively with other developers and stakeholders to deliver high-quality software products on time. Please provide detailed instructions on how to perform these tasks effectively.";
    public const string DEFAULT_FINE_TUNED_MODEL = "Convert the following Unity C# script to BabylonJS TypeScript according to the rules defined during fine tuning. The converted script should use be wrapped in a namespace called PROJECT. If the Unity C# script is a MonoBehaviour class or a EditorScriptComponent class, which is a subclass of MonoBehaviour, the converted script should derive from TOOLKIT.ScriptComponent, a class that provides similar life cycle functions so Awake() should convert to awake() and Start() to start() and Update() to update() and LateUpdate() to late() and FixedUpdate to fixed() and so on. Do not create life cycle functions that do not exist in source script. Do not create an empty constructor. Do not include any code block formatting like ```typescript, just return the raw code as plain text. Do not use TypeScript import or require. Ignore any C# class or property attributes. Ignore the C# OnUpdateProperties function. Ignore any C# CustomEditor classes. Dont comment on anything that was ignored.";
    public const string DEFAULT_SCRIPT_CONVERTER = "You are a code converter that will be provided with a Unity C# script file, and your task is to convert it to a BabylonJS TypeScript file. The converted script should use be wrapped in a namespace called PROJECT. If the Unity C# script is a MonoBehaviour class or a EditorScriptComponent class, which is a subclass of MonoBehaviour, the converted script should derive from TOOLKIT.ScriptComponent, a class that provides similar life cycle functions so Awake() should convert to awake() and Start() to start() and Update() to update() and LateUpdate() to late() and FixedUpdate to fixed() and so on. Do not create life cycle functions that do not exist in source script. Do not create an empty constructor. Do not include any code block formatting like ```typescript, just return the raw code as plain text. Do not use TypeScript import or require. Ignore any C# class or property attributes. Ignore the C# OnUpdateProperties function. Ignore any C# CustomEditor classes. Dont comment on anything that was ignored.";
    public const string DEFAULT_TRAINING_EXAMPLE = "is a training example for converting Unity C# code to BabylonJS TypeScript code.";
    public const string DEFAULT_ORGANIZATION_ID = "Default Organization";
    public const string DEFAULT_PROJECT_NAME = "Default Project";
    public const int MINIMUM_ALLOWED_TOKENS = 0;
    public const int MAXIMUM_ALLOWED_TOKENS = 65536;
    public static int NETWORK_BUFFER_SIZE = 8192; // Note: Default Network Streaming Buffer Size (8KB)
    public static int NETWORK_TIMEOUT_MINUTES = 30; // Note: Default Network Timeout Minutes (15)

    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Developer Options/AI Code Assistant", false, 72)]
    static void AI_OpenAssistant()
    {
        CanvasTools.CanvasToolsExporter.Initialize();
        AI_CodeAssistant assistant = ScriptableObject.CreateInstance<AI_CodeAssistant>();
        assistant.OnInitialize();
        assistant.ShowUtility();
    }
    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Developer Options/AI Chat Completions", false, 84)]
    static void AI_ChatCompletion()
    {
        CanvasTools.CanvasToolsExporter.Initialize();
        Application.OpenURL(AI_CodeAssistant.DASHBOARD_COMPLETION);
    }
    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Developer Options/AI Training Dashboard", false, 85)]
    static void AI_DashboardTraining()
    {
        CanvasTools.CanvasToolsExporter.Initialize();
        Application.OpenURL(AI_CodeAssistant.DASHBOARD_FINETUNING);
    }
    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Developer Options/AI Secret Project Keys", false, 86)]
    static void AI_APIKeyGeneration()
    {
        CanvasTools.CanvasToolsExporter.Initialize();
        Application.OpenURL(AI_CodeAssistant.DASHBOARD_DEV_APIKEYS);
    }
    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Developer Options/AI Code Assistant Editor", false, 87)]
    static void AI_VSCodeExtension()
    {
        CanvasTools.CanvasToolsExporter.Initialize();
        Application.OpenURL(AI_CodeAssistant.CODEWRX_AI_EXTENSION);
    }
    [MenuItem("Assets/Convert", false, 14)] // The action
    static void AI_ConvertScriptComponent()
    {
        // Get the selected file
        var obj = Selection.activeObject;
        MonoScript monoscript = null;
        if (obj is MonoBehaviour monoBehaviour)
        {
            monoscript = MonoScript.FromMonoBehaviour(monoBehaviour);
            // DEBUG: UnityEngine.Debug.Log($"DEBUG: Processing MonoBehaviour script: {monoscript.name}");
        }        
        else if (obj is MonoScript monoScript)
        {
            monoscript = monoScript;
            // DEBUG: UnityEngine.Debug.Log($"DEBUG: Processing MonoBehaviour script: {monoScript.name}");
        }

        string asset = AssetDatabase.GetAssetPath(obj);
        if (System.String.IsNullOrEmpty(asset)) return;
        
        string path = Path.GetFullPath(asset);
        if (System.String.IsNullOrEmpty(path)) return;

        // Process the script file here (For example, log it, open it, modify it, etc.)
        // UnityEngine.Debug.Log($"Processing MonoBehaviour script: {path}");

        // Perform your desired action here
        // For example, open the script in the default editor or modify it
        // For now, we will simply log a message

        string script = File.ReadAllText(path);
        // .. 
        // TODO: Validate Is Proper Script Content
        // ..
        CanvasTools.CanvasToolsExporter.Initialize();
        AI_ScriptConverter converter = ScriptableObject.CreateInstance<AI_ScriptConverter>();
        converter.OnInitialize(path, script, monoscript);
        converter.ShowUtility();
    }
    [MenuItem("Assets/Convert", true)] // Validation function
    static bool AI_ValidateConvertScriptComponent()
    {
        var obj = Selection.activeObject;
        if (obj == null) return false;
        
        string asset = AssetDatabase.GetAssetPath(obj);
        if (System.String.IsNullOrEmpty(asset)) return false;
        
        string path = Path.GetFullPath(asset);
        if (System.String.IsNullOrEmpty(path)) return false;

        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.IndexOf("AI_CodeAssistant", StringComparison.OrdinalIgnoreCase) > 0) return false;
        if (path.IndexOf("AI_DataConverter", StringComparison.OrdinalIgnoreCase) > 0) return false;
        if (path.IndexOf("AI_ScriptConverter", StringComparison.OrdinalIgnoreCase) > 0) return false;

        // Check if the file contains MonoBehaviour to limit the action only to MonoBehaviour scripts
        // string script = File.ReadAllText(path);
        // return (script.Contains(": MonoBehaviour") || script.Contains(": EditorScriptComponent"));
        return true; // Note: Convert Any C# Script To Babylon Toolkit
    }
    // public static OpenAICompletions ReadOpenAICopilot()
    // {
    //     return (OpenAICompletions)EditorPrefs.GetInt("OpenAICopilot", (int)OpenAICompletions.CodeAssistant);
    // }
    // public static void SaveOpenAICopilot(OpenAICompletions copilot)
    // {
    //     EditorPrefs.SetInt("OpenAICopilot", (int)copilot);
    // }
    public static string ReadOpenAIAPIKey()
    {
        return EditorPrefs.GetString("OpenAIAPIKey");
    }
    public static void SaveOpenAIAPIKey(string key)
    {
        EditorPrefs.SetString("OpenAIAPIKey", key);
    }
    public static string ReadOpenAIORGKey()
    {
        return EditorPrefs.GetString("OpenAIORGKey", AI_CodeAssistant.DEFAULT_ORGANIZATION_ID);
    }
    public static void SaveOpenAIORGKey(string org)
    {
        EditorPrefs.SetString("OpenAIORGKey", org);
    }
    public static string ReadOpenAIProject()
    {
        return EditorPrefs.GetString("OpenAIProject", AI_CodeAssistant.DEFAULT_PROJECT_NAME);
    }
    public static void SaveOpenAIProject(string name)
    {
        EditorPrefs.SetString("OpenAIProject", name);
    }
    public static OpenAILogicModel ReadOpenAIModel()
    {
        return (OpenAILogicModel)EditorPrefs.GetInt("OpenAIModel", (int)OpenAILogicModel.gpt4o_mini);
    }
    public static void SaveOpenAIModel(OpenAILogicModel model)
    {
        EditorPrefs.SetInt("OpenAIModel", (int)model);
    }
    public static OpenAISourceControl ReadOpenAISource()
    {
        return (OpenAISourceControl)EditorPrefs.GetInt("OpenAISource", (int)OpenAISourceControl.DefaultModel);
    }
    public static void SaveOpenAISource(OpenAISourceControl scm)
    {
        EditorPrefs.SetInt("OpenAISource", (int)scm);
    }
    public static OpenAIPseudoCode ReadOpenAIPseudoCode()
    {
        return (OpenAIPseudoCode)EditorPrefs.GetInt("OpenAIPseudoCode", (int)OpenAIPseudoCode.LiveScript);
    }
    public static void SaveOpenAIPseudoCode(OpenAIPseudoCode code)
    {
        EditorPrefs.SetInt("OpenAIPseudoCode", (int)code);
    }
    public static string ReadOpenAIModelName()
    {
        return EditorPrefs.GetString("OpenAIModelName", AI_CodeAssistant.GPT4O_MINI);
    }
    public static void SaveOpenAIModelName(string name)
    {
        EditorPrefs.SetString("OpenAIModelName", name);
    }
    public static float ReadOpenAITemperature()
    {
        return EditorPrefs.GetFloat("OpenAITemperature", 0.1f);
    }
    public static void SaveOpenAITemperature(float temperature)
    {
        EditorPrefs.SetFloat("OpenAITemperature", temperature);
    }
    public static int ReadOpenAIMaxTokens()
    {
        return EditorPrefs.GetInt("OpenAIMaxTokens", 0);
    }
    public static void SaveOpenAIMaxTokens(int tokens)
    {
        EditorPrefs.SetInt("OpenAIMaxTokens", tokens);
    }
    public static float ReadOpenAITopp()
    {
        return EditorPrefs.GetFloat("OpenAITopp", 1.0f);
    }
    public static void SaveOpenAITopp(float topp)
    {
        EditorPrefs.SetFloat("OpenAITopp", topp);
    }
    public static float ReadOpenAIPPenalty()
    {
        return EditorPrefs.GetFloat("OpenAIPPenalty", 0.0f);
    }
    public static void SaveOpenAIPPenalty(float penalty)
    {
        EditorPrefs.SetFloat("OpenAIPPenalty", penalty);
    }
    public static float ReadOpenAIFPenalty()
    {
        return EditorPrefs.GetFloat("OpenAIFPenalty", 0.0f);
    }
    public static void SaveOpenAIFPenalty(float penalty)
    {
        EditorPrefs.SetFloat("OpenAIFPenalty", penalty);
    }
    public static string ReadOpenAIPersona()
    {
        return EditorPrefs.GetString("OpenAIPersona");
    }
    public static void SaveOpenAIPersona(string persona)
    {
        EditorPrefs.SetString("OpenAIPersona", persona);
    }
    public static void DeleteOpenAIPersona()
    {
        EditorPrefs.DeleteKey("OpenAIPersona");
    }
    public static string ReadOpenAITraining()
    {
        return EditorPrefs.GetString("OpenAITraining");
    }
    public static void SaveOpenAITraining(string path)
    {
        EditorPrefs.SetString("OpenAITraining", path);
    }
    public static void DeleteOpenAITraining()
    {
        EditorPrefs.DeleteKey("OpenAITraining");
    }
    public static string ReadOpenAIPrompt()
    {
        return EditorPrefs.GetString("OpenAIPrompt");
    }
    public static void SaveOpenAIPrompt(string path)
    {
        EditorPrefs.SetString("OpenAIPrompt", path);
    }
    public static void DeleteOpenAIPrompt()
    {
        EditorPrefs.DeleteKey("OpenAIPrompt");
    }
    public static string EscapeDefaultJsonString(string text)
    {
        return text.Replace("\\", "\\\\")   // Escape backslashes
                   .Replace("\"", "\\\"")   // Escape double quotes
                   .Replace("\n", "\\n")    // Escape newlines
                   .Replace("\r", "\\r")    // Escape carriage returns
                   .Replace("\t", "\\t");   // Escape tabs
    }
    public static string EscapeTemplateJsonString(string text)
    {
        return text.Replace("\n", "\\n")    // Escape newlines
                   .Replace("\r", "\\r")    // Escape carriage returns
                   .Replace("\t", "\\t");   // Escape tabs
    }
    public static HttpStatusCode GetHttpStatusCode(System.Exception err)
    {
        if (err is WebException)
        {
            WebException we = (WebException)err;
            if (we.Response is HttpWebResponse)
            {
                HttpWebResponse response = (HttpWebResponse)we.Response;
                return response.StatusCode;
            }
        }
        return 0;
    }    
    public static string GetHttpStatusDescription(System.Exception err)
    {
        if (err is WebException)
        {
            WebException we = (WebException)err;
            if (we.Response is HttpWebResponse)
            {
                HttpWebResponse response = (HttpWebResponse)we.Response;
                return response.StatusDescription;
            }
        }
        return "Unknown";
    }
    public static async Task<string> PostTrainingFileAsync(string endpoint, string apikey, string orgkey, string project, string filePath)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            // set max timeout
            httpClient.Timeout = TimeSpan.FromMinutes(AI_CodeAssistant.NETWORK_TIMEOUT_MINUTES);

            // Set the Authorization And Organization headers
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apikey);
            if (!String.IsNullOrEmpty(orgkey) && !orgkey.Equals(AI_CodeAssistant.DEFAULT_ORGANIZATION_ID, StringComparison.OrdinalIgnoreCase)) httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", orgkey);
            if (!String.IsNullOrEmpty(project) && !project.Equals(AI_CodeAssistant.DEFAULT_PROJECT_NAME, StringComparison.OrdinalIgnoreCase)) httpClient.DefaultRequestHeaders.Add("OpenAI-Project", project);

            // Create MultipartFormDataContent
            using (var form = new MultipartFormDataContent())
            {
                // Open the file stream with useAsync set to true
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, AI_CodeAssistant.NETWORK_BUFFER_SIZE, useAsync: true))
                {
                    // Create StreamContent from the file stream with specified buffer size
                    var fileContent = new StreamContent(fileStream, AI_CodeAssistant.NETWORK_BUFFER_SIZE);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                    // Add the file content to the form data
                    form.Add(fileContent, "file", Path.GetFileName(filePath));

                    // Add the 'purpose' parameter
                    form.Add(new StringContent("fine-tune"), "purpose");

                    // Post the form data to the endpoint
                    HttpResponseMessage response = await httpClient.PostAsync(endpoint, form);

                    // Ensure the request succeeded
                    response.EnsureSuccessStatusCode();

                    // Read and return the response content as a string
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }
    }
    public static async Task<string> PostSafeWebRequestAsync(string endpoint, string apikey, string orgkey, string project, string payload)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            // set max timeout
            httpClient.Timeout = TimeSpan.FromMinutes(AI_CodeAssistant.NETWORK_TIMEOUT_MINUTES);

            // Set the Authorization And Organization headers
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apikey);
            if (!String.IsNullOrEmpty(orgkey) && !orgkey.Equals(AI_CodeAssistant.DEFAULT_ORGANIZATION_ID, StringComparison.OrdinalIgnoreCase)) httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", orgkey);
            if (!String.IsNullOrEmpty(project) && !project.Equals(AI_CodeAssistant.DEFAULT_PROJECT_NAME, StringComparison.OrdinalIgnoreCase)) httpClient.DefaultRequestHeaders.Add("OpenAI-Project", project);

            // Convert the string payload to a stream
            using (Stream stream = AI_CodeAssistant.GenerateStreamFromString(payload))
            {
                // Create StreamContent from the stream
                using (StreamContent content = new StreamContent(stream))
                {
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                    // Send the POST request asynchronously
                    HttpResponseMessage response = await httpClient.PostAsync(endpoint, content);

                    // Ensure the response indicates success
                    response.EnsureSuccessStatusCode();

                    // Read and return the response content as a string
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }
    }
    public static async Task<string> GetSafeWebRequestAsync(string endpoint, string apikey, string orgkey, string project)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            // set max timeout
            httpClient.Timeout = TimeSpan.FromMinutes(AI_CodeAssistant.NETWORK_TIMEOUT_MINUTES);

            // Set the Authorization And Organization headers
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apikey);
            if (!String.IsNullOrEmpty(orgkey) && !orgkey.Equals(AI_CodeAssistant.DEFAULT_ORGANIZATION_ID, StringComparison.OrdinalIgnoreCase)) httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", orgkey);
            if (!String.IsNullOrEmpty(project) && !project.Equals(AI_CodeAssistant.DEFAULT_PROJECT_NAME, StringComparison.OrdinalIgnoreCase)) httpClient.DefaultRequestHeaders.Add("OpenAI-Project", project);

            // Send the GET request asynchronously
            HttpResponseMessage response = await httpClient.GetAsync(endpoint);

            // Ensure the response indicates success
            response.EnsureSuccessStatusCode();

            // Read and return the response content as a string
            return await response.Content.ReadAsStringAsync();
        }
    }
    public static async Task<string> DeleteWebRequestAsync(string endpoint, string apikey, string orgkey, string project, string model)
    {
        string url = $"{endpoint}/{model}";
        using (HttpClient httpClient = new HttpClient())
        {
            // set max timeout
            httpClient.Timeout = TimeSpan.FromMinutes(AI_CodeAssistant.NETWORK_TIMEOUT_MINUTES);

            // Set the Authorization And Organization headers
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apikey);
            if (!String.IsNullOrEmpty(orgkey) && !orgkey.Equals(AI_CodeAssistant.DEFAULT_ORGANIZATION_ID, StringComparison.OrdinalIgnoreCase)) httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", orgkey);
            if (!String.IsNullOrEmpty(project) && !project.Equals(AI_CodeAssistant.DEFAULT_PROJECT_NAME, StringComparison.OrdinalIgnoreCase)) httpClient.DefaultRequestHeaders.Add("OpenAI-Project", project);

            // Send the DELETE request asynchronously
            HttpResponseMessage response = await httpClient.DeleteAsync(url);

            // Ensure the response indicates success
            response.EnsureSuccessStatusCode();

            // Read and return the response content as a string
            return await response.Content.ReadAsStringAsync();
        }
    }
    public static async Task DownloadFileAsync(string fileUrl, string destinationPath)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            // set max timeout
            httpClient.Timeout = TimeSpan.FromMinutes(AI_CodeAssistant.NETWORK_TIMEOUT_MINUTES);

            // Send the request and get the response
            using (HttpResponseMessage response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                // Ensure the request was successful
                response.EnsureSuccessStatusCode();

                // Open the response stream
                using (Stream responseStream = await response.Content.ReadAsStreamAsync())
                {
                    // Open the file stream to write to disk
                    using (FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, AI_CodeAssistant.NETWORK_BUFFER_SIZE, useAsync: true))
                    {
                        // Stream the content from the response to the file
                        await responseStream.CopyToAsync(fileStream);
                    }
                }
            }
        }
    }
    public static Stream GenerateStreamFromString(string payload)
    {
        byte[] byteArray = Encoding.UTF8.GetBytes(payload);
        return new MemoryStream(byteArray);
    }
    public static string GetAssetPathFromFullPath(string fullPath)
    {
        // Normalize path separators
        fullPath = fullPath.Replace("\\", "/");
        string dataPath = Application.dataPath.Replace("\\", "/");

        if (fullPath.StartsWith(dataPath))
        {
            // Extract the relative path starting from "Assets"
            string assetPath = "Assets" + fullPath.Substring(dataPath.Length);
            return assetPath;
        }
        else
        {
            Debug.LogError("The provided path is not inside the project's Assets folder.");
            return null;
        }
    }
    public static string GetLogicModelName(OpenAILogicModel logicModel, string fineTunedModel = null)
    {
        string aimodel = AI_CodeAssistant.GPT4O_MINI;
        switch (logicModel)
        {
            case OpenAILogicModel.gpt35_turbo:
                aimodel = AI_CodeAssistant.GPT35_TURBO;
                break;
            case OpenAILogicModel.gpt4o_mini:
                aimodel = AI_CodeAssistant.GPT4O_MINI;
                break;
            case OpenAILogicModel.gpt4o:
                aimodel = AI_CodeAssistant.GPT4O; 
                break;
            case OpenAILogicModel.o1preview:
                aimodel = AI_CodeAssistant.O1_PREVIEW; 
                break;
            case OpenAILogicModel.o1preview_mini:
                aimodel = AI_CodeAssistant.O1_PREVIEW_MINI; 
                break;
            case OpenAILogicModel.gptX:
                if (String.IsNullOrEmpty(fineTunedModel))
                {
                    aimodel = AI_CodeAssistant.GPT4O_MINI;
                    UnityEngine.Debug.LogWarning("WARNING - No Fine Tuned Model Name Specified: Using Default Model");
                }
                else
                {
                    aimodel = fineTunedModel;
                }
                break;
        }
        return aimodel;
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Editor Windows Functions
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    
    private string openAIAPIKey = null;
    private string openAIORGKey = AI_CodeAssistant.DEFAULT_ORGANIZATION_ID;
    private string openAIProject = AI_CodeAssistant.DEFAULT_PROJECT_NAME;
    private bool showPassword = false;    
    //private OpenAICompletions copilotCompletions = OpenAICompletions.CodeAssistant;
    private int maxTokens = 0;
    private float temperature = 0.1f;
    private float topp = 1.0f;
    private float presencePenalty = 0.0f;
    private float frequencyPenalty = 0.0f;
    private string fineTunedModel = AI_CodeAssistant.GPT4O_MINI;
    private OpenAILogicModel logicModel = OpenAILogicModel.gpt4o_mini;
    private int selectedIndex = 0;
    private bool selectMode = false;
    private string personaPath = null;
    private TextAsset personaAsset = null;
    private static string[] FineTunedModels = new string[] { AI_CodeAssistant.GPT4O_MINI };
    private static bool FineTunedModelsLoaded = false;
    private static Texture2D EditIcon = null;
    private static Texture2D RefreshIcon = null;
    private static Texture2D DeleteIcon = null;
    private static Texture2D CancelIcon = null;
    private static Texture2D SelectIcon = null;

    public void OnInitialize()
    {
        if (AI_CodeAssistant.EditIcon == null)
        {
            AI_CodeAssistant.EditIcon = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image as Texture2D;
            // AI_CodeAssistant.EditIcon = EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow").image as Texture2D;
            // AI_CodeAssistant.EditIcon = EditorGUIUtility.IconContent("d_UnityEditor.HierarchyWindow").image as Texture2D;
			// AI_CodeAssistant.EditIcon = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image as Texture2D;
			// AI_CodeAssistant.EditIcon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image as Texture2D;
			// AI_CodeAssistant.EditIcon = CanvasTools.ExporterUtils.copyTexture(AI_CodeAssistant.EditIcon);
            // AI_CodeAssistant.EditIcon.Scale(16, 14);
            // UnityEngine.Debug.LogFormat("Edit Icon Size: {0} x {1}", AI_CodeAssistant.EditIcon.width, AI_CodeAssistant.EditIcon.height);
        }
        if (AI_CodeAssistant.RefreshIcon == null)
        {
            AI_CodeAssistant.RefreshIcon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image as Texture2D;
			// AI_CodeAssistant.RefreshIcon = CanvasTools.ExporterUtils.copyTexture(AI_CodeAssistant.RefreshIcon);
            // AI_CodeAssistant.RefreshIcon.Scale(16, 14);
            // UnityEngine.Debug.LogFormat("Refresh Icon Size: {0} x {1}", AI_CodeAssistant.RefreshIcon.width, AI_CodeAssistant.RefreshIcon.height);
        }
        if (AI_CodeAssistant.DeleteIcon == null)
        {
            AI_CodeAssistant.DeleteIcon = EditorGUIUtility.IconContent("d_UnityEditor.HierarchyWindow").image as Texture2D;
			// AI_CodeAssistant.DeleteIcon = CanvasTools.ExporterUtils.copyTexture(AI_CodeAssistant.DeleteIcon);
            // AI_CodeAssistant.DeleteIcon.Scale(16, 14);
            // UnityEngine.Debug.LogFormat("Delete Icon Size: {0} x {1}", AI_CodeAssistant.DeleteIcon.width, AI_CodeAssistant.DeleteIcon.height);
        }
        if (AI_CodeAssistant.CancelIcon == null)
        {
            AI_CodeAssistant.CancelIcon = EditorGUIUtility.IconContent("d_UnityEditor.HierarchyWindow").image as Texture2D;
			// AI_CodeAssistant.CancelIcon = CanvasTools.ExporterUtils.copyTexture(AI_CodeAssistant.CancelIcon);
            // AI_CodeAssistant.CancelIcon.Scale(16, 14);
            // UnityEngine.Debug.LogFormat("Cancel Icon Size: {0} x {1}", AI_CodeAssistant.CancelIcon.width, AI_CodeAssistant.CancelIcon.height);
        }
        if (AI_CodeAssistant.SelectIcon == null)
        {
            AI_CodeAssistant.SelectIcon = EditorGUIUtility.IconContent("d_UnityEditor.HierarchyWindow").image as Texture2D;
			// AI_CodeAssistant.SelectIcon = CanvasTools.ExporterUtils.copyTexture(AI_CodeAssistant.SelectIcon);
            // AI_CodeAssistant.SelectIcon.Scale(16, 14);
            // UnityEngine.Debug.LogFormat("Select Icon Size: {0} x {1}", AI_CodeAssistant.SelectIcon.width, AI_CodeAssistant.SelectIcon.height);
        }
        this.logicModel = AI_CodeAssistant.ReadOpenAIModel();
        this.sourceControl = AI_CodeAssistant.ReadOpenAISource();
        this.openAIAPIKey = AI_CodeAssistant.ReadOpenAIAPIKey();
        this.openAIORGKey = AI_CodeAssistant.ReadOpenAIORGKey();
        this.openAIProject = AI_CodeAssistant.ReadOpenAIProject();
        this.maxTokens = AI_CodeAssistant.ReadOpenAIMaxTokens();
        this.temperature = AI_CodeAssistant.ReadOpenAITemperature();
        this.topp = AI_CodeAssistant.ReadOpenAITopp();
        this.presencePenalty = AI_CodeAssistant.ReadOpenAIPPenalty();
        this.frequencyPenalty = AI_CodeAssistant.ReadOpenAIFPenalty();
        this.fineTunedModel = AI_CodeAssistant.ReadOpenAIModelName();
        this.personaPath = AI_CodeAssistant.ReadOpenAIPersona();
        //this.copilotCompletions = AI_CodeAssistant.ReadOpenAICopilot();
        if (String.IsNullOrEmpty(this.fineTunedModel))
        {
            this.fineTunedModel = AI_CodeAssistant.GPT4O_MINI;
        }
        float dialogHeight = (this.logicModel == OpenAILogicModel.gptX) ? 436.0f : 388.0f;
        maxSize = new Vector2(604.0f, dialogHeight);
        minSize = this.maxSize;
        if (AI_CodeAssistant.FineTunedModelsLoaded == false)
        {
            RefreshFineTunedModels();
        }
    }

    void OnEnable()
    {
        titleContent = new GUIContent("AI Code Assistant Console");
    }

    private OpenAISourceControl sourceControl = OpenAISourceControl.DefaultModel;
    public void OnGUI()
    {
        float dialogHeight = (this.logicModel == OpenAILogicModel.gptX) ? 436.0f : 388.0f;
        if (this.maxSize.y != dialogHeight)
        {
            this.maxSize = new Vector2(604.0f, dialogHeight);
            this.minSize = this.maxSize;
        }
        UnityTools.HelpBoxLink("Please visit <color='#00EE00'>codewrx.ai</color> for more information regarding the use of our fine tuned models with the <color='#00EE00'>Babylon Toolkit</color>.", AI_CodeAssistant.CODEWRX_AI_DASHBOARD, MessageType.Info);
        EditorGUILayout.Space();
        logicModel = (OpenAILogicModel)EditorGUILayout.EnumPopup("Open AI Model:", (OpenAILogicModel)logicModel, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();
        if (logicModel == OpenAILogicModel.gptX)
        {
            if (this.selectMode == true)
            {
                DrawFineTuneSelectMode();
            }
            else
            {
                DrawFineTuneDisplayMode();
            }
        }

        EditorGUI.BeginDisabledGroup(logicModel == OpenAILogicModel.gptX && this.selectMode == true);
            temperature = (float)EditorGUILayout.Slider("Temperature:", temperature, 0.0f, 2.0f);
            EditorGUILayout.Space();

            topp = (float)EditorGUILayout.Slider("Top Probability:", topp, 0.0f, 10.0f);
            EditorGUILayout.Space();

            presencePenalty = (float)EditorGUILayout.Slider("Presence Penalty:", presencePenalty, 0.0f, 2.0f);
            EditorGUILayout.Space();

            frequencyPenalty = (float)EditorGUILayout.Slider("Frequency Penalty:", frequencyPenalty, 0.0f, 2.0f);
            EditorGUILayout.Space();

            maxTokens = (int)EditorGUILayout.Slider("Max Token Usage:", maxTokens, AI_CodeAssistant.MINIMUM_ALLOWED_TOKENS, AI_CodeAssistant.MAXIMUM_ALLOWED_TOKENS);
            EditorGUILayout.Space();

            sourceControl = (OpenAISourceControl)EditorGUILayout.EnumPopup("Code Completions:", (OpenAISourceControl)sourceControl, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();

            personaAsset = (TextAsset)EditorGUILayout.ObjectField("Developer Persona:", personaAsset, typeof(TextAsset), false);
            EditorGUILayout.Space();

            openAIORGKey = EditorGUILayout.TextField("My Organization ID:", openAIORGKey, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();

            openAIProject = EditorGUILayout.TextField("My Project ID:", openAIProject, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();

            openAIAPIKey = TogglePasswordField("My User Key:", openAIAPIKey, ref showPassword);
            EditorGUILayout.Space();

            //copilotCompletions = (OpenAICompletions)EditorGUILayout.EnumPopup("My Developer AI:", (OpenAICompletions)copilotCompletions, GUILayout.ExpandWidth(true));
            //EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Code Assistant Settings"))
            {
                if (UnityTools.ShowMessage("Are you sure want to reset the code assistant settings?", CanvasToolsStatics.CANVAS_TOOLS_TITLE, "Yes", "No"))
                {
                    this.logicModel = OpenAILogicModel.gpt4o_mini;
                    this.fineTunedModel = AI_CodeAssistant.GPT4O_MINI;
                    SaveAssistantProperties();
                    UnityTools.ShowMessage("Code assistant settings reset successfully.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
                }
            }

            if (GUILayout.Button("Save Code Assistant Settings"))
            {
                if (String.IsNullOrEmpty(this.fineTunedModel))
                {
                    this.fineTunedModel = AI_CodeAssistant.GPT4O_MINI;
                }
                SaveAssistantProperties();
                UnityTools.ShowMessage("Code assistant settings saved successfully.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
                this.Close();
            }
            EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private string TogglePasswordField(string label, string text, ref bool showPassword)
    {
        EditorGUILayout.BeginHorizontal();
        
        if (showPassword)
        {
            text = EditorGUILayout.TextField(label, text);
        }
        else
        {
            text = EditorGUILayout.PasswordField(label, text);
        }

        // GUILayout.Space(-5); // Adjust this value to control the space before the button
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.margin.top = 1;
        buttonStyle.margin.bottom = 0;        
        if (GUILayout.Button(showPassword ? "Hide" : "Show", buttonStyle, GUILayout.Width(51), GUILayout.Height(19)))
        {
            showPassword = !showPassword;
        }

        EditorGUILayout.EndHorizontal();
        return text;
    }

    public void OnInspectorUpdate()
    {
        string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        string folderPath = System.IO.Path.GetDirectoryName(scriptPath);
        if (personaAsset == null)
        {
            string personaFile = "DefaultDeveloperPersona.txt";
            string defaultPath = (folderPath + "/" + personaFile);
            string templatePath = defaultPath;
            if (!System.String.IsNullOrEmpty(this.personaPath))
            {
                templatePath = this.personaPath;
            }
            try
            {
                personaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(templatePath);
            }
            catch /*(Exception e)*/
            {
                // DEBUG: UnityEngine.Debug.LogErrorFormat("DEBUG: Load Prompt Asset At Path Error: {0}", e.Message);
            }
            // ..
            // Double Check Has Valid Prompt Asset
            // ..
            if (personaAsset == null)
            {
                try
                {
                    personaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(defaultPath);
                }
                catch /*(Exception e)*/
                {
                    // DEBUG: UnityEngine.Debug.LogErrorFormat("DEBUG: Load Prompt Asset At Path Error: {0}", e.Message);
                }
            }
            // ..
            // Mark the scene as dirty so the change is saved
            // ..
            if (personaAsset != null)
            {
                EditorUtility.SetDirty(this);
            }
            else
            {
                // DEBUG: UnityEngine.Debug.LogWarningFormat("DEBUG: Failed To Load Prompt Asset File At: {0}", templatePath);
            }
        }
        this.Repaint();
    }

    private void SaveAssistantProperties()
    {
        AI_CodeAssistant.SaveOpenAIModel(this.logicModel);
        AI_CodeAssistant.SaveOpenAISource(this.sourceControl);
        AI_CodeAssistant.SaveOpenAIAPIKey(this.openAIAPIKey);
        AI_CodeAssistant.SaveOpenAIORGKey(this.openAIORGKey);
        AI_CodeAssistant.SaveOpenAIProject(this.openAIProject);
        AI_CodeAssistant.SaveOpenAITemperature(this.temperature);
        AI_CodeAssistant.SaveOpenAIMaxTokens(this.maxTokens);
        AI_CodeAssistant.SaveOpenAITopp(this.topp);
        AI_CodeAssistant.SaveOpenAIPPenalty(this.presencePenalty);
        AI_CodeAssistant.SaveOpenAIFPenalty(this.frequencyPenalty);
        AI_CodeAssistant.SaveOpenAIModelName(this.fineTunedModel);
        //AI_CodeAssistant.SaveOpenAICopilot(this.copilotCompletions);
        string aiLogicModel = AI_CodeAssistant.GetLogicModelName(this.logicModel, this.fineTunedModel);
        CanvasToolsExporter.DefaultFineTunedModel = aiLogicModel;
        CanvasToolsExporter.SourceControlFineTunedModel = aiLogicModel;
        string personaContent = (this.personaAsset != null) ? AI_CodeAssistant.EscapeDefaultJsonString(this.personaAsset.text) : AI_CodeAssistant.DEFAULT_DEVELOPER_PERSONA;
        //bool githubCompletions = (this.copilotCompletions == OpenAICompletions.GithubCopilot);
        bool githubCompletions = (this.sourceControl == OpenAISourceControl.GithubCopilot);
        AI_CodeAssistant.UpdateAICodeSettings(this.logicModel, this.sourceControl, this.temperature, this.maxTokens, this.topp, this.presencePenalty, this.frequencyPenalty, personaContent, githubCompletions);
    }

    private void DrawFineTuneDisplayMode()
    {
        EditorGUI.BeginDisabledGroup(true);
        fineTunedModel = EditorGUILayout.TextField("Model Identifier:", fineTunedModel, GUILayout.ExpandWidth(true));
        EditorGUI.EndDisabledGroup();
        // ..
        // WTF: The HyperLink Styling Only Works If New GUIStyle Each Update
        // ..
        GUIStyle hyperlinkStyle1 = new GUIStyle(GUI.skin.label);
        hyperlinkStyle1.normal.textColor = Color.yellow; // Yellow color for hyperlink
        hyperlinkStyle1.hover.textColor = Color.cyan; // Change color on hover
        hyperlinkStyle1.fontSize = 10;
        hyperlinkStyle1.fontStyle = FontStyle.Normal;
        hyperlinkStyle1.wordWrap = false;
        // Remove padding between icon and text
        hyperlinkStyle1.padding.left = 0;
        hyperlinkStyle1.margin.left = 0;        
        // Create a horizontal layout and push the link to the right
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace(); // Pushes the content to the right
        GUILayout.Label("|", GUILayout.Width(0)); // This is the separator
        if (GUILayout.Button(new GUIContent("Manage Fine Tuned Models ", AI_CodeAssistant.EditIcon), hyperlinkStyle1, GUILayout.ExpandWidth(false)))
        {
            UpdateSelectedModelIndex();
            this.selectMode = true;
        }
        EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        // ..
        // Optionally, add underline effect (available in newer Unity versions)
        // Handles.BeginGUI();
        // Handles.color = Color.blue;
        // Handles.DrawLine(new Vector3(rect.xMin, rect.yMax, 0), new Vector3(rect.xMax, rect.yMax, 0));
        // Handles.EndGUI();
        // ..
        GUILayout.EndHorizontal(); // End the horizontal layout
        EditorGUILayout.Space();
    }

    private void DrawFineTuneSelectMode()
    {
        selectedIndex = EditorGUILayout.Popup("Model Identifier:", selectedIndex, AI_CodeAssistant.FineTunedModels);
        // ..
        // WTF: The HyperLink Styling Only Works If New GUIStyle Each Update
        // ..
        GUIStyle hyperlinkStyle2 = new GUIStyle(GUI.skin.label);
        hyperlinkStyle2.normal.textColor = Color.yellow; // Yellow color for hyperlink
        hyperlinkStyle2.hover.textColor = Color.cyan; // Change color on hover
        hyperlinkStyle2.fontSize = 10;
        hyperlinkStyle2.fontStyle = FontStyle.Normal;
        hyperlinkStyle2.wordWrap = false;
        // Remove padding between icon and text
        hyperlinkStyle2.padding.left = 0;
        hyperlinkStyle2.margin.left = 0;        
        // Create a horizontal layout and push the link to the right
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(145)); // This is the separator
        if (GUILayout.Button(new GUIContent("Refresh Fine Tuned Models", AI_CodeAssistant.RefreshIcon), hyperlinkStyle2, GUILayout.ExpandWidth(false)))
        {
            RefreshFineTunedModels();
            // TODO: Disable Button For Default Refresh Timeout Period
        }
        EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        GUILayout.FlexibleSpace(); // Pushes the content to the right
        // ..
        GUILayout.Label("|", GUILayout.Width(6)); // This is the separator
        if (GUILayout.Button(new GUIContent("Delete", AI_CodeAssistant.DeleteIcon), hyperlinkStyle2, GUILayout.ExpandWidth(false)))
        {
            if (this.selectedIndex > 0)
            {
                DeleteTrainingDataset();
            }
            else
            {
                UnityTools.ShowMessage("You cannot delete the default fine tuned model.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
            }
        }
        EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        // ..
        GUILayout.Label("|", GUILayout.Width(6)); // This is the separator
        if (GUILayout.Button(new GUIContent("Cancel", AI_CodeAssistant.CancelIcon), hyperlinkStyle2, GUILayout.ExpandWidth(false)))
        {
            this.selectMode = false;
        }
        EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        // ..
        GUILayout.Label("|", GUILayout.Width(6)); // This is the separator
        if (GUILayout.Button(new GUIContent("Select ", AI_CodeAssistant.SelectIcon), hyperlinkStyle2, GUILayout.ExpandWidth(false)))
        {
            if (AI_CodeAssistant.FineTunedModels.Length > this.selectedIndex)
            {
                if (UnityTools.ShowMessage("Are you sure want to select that model?", CanvasToolsStatics.CANVAS_TOOLS_TITLE, "Yes", "No"))
                {
                    this.fineTunedModel = AI_CodeAssistant.FineTunedModels[this.selectedIndex];
                    SaveAssistantProperties();
                    this.selectMode = false;
                }
            }
        }
        EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        // ..
        // Optionally, add underline effect (available in newer Unity versions)
        // Handles.BeginGUI();
        // Handles.color = Color.blue;
        // Handles.DrawLine(new Vector3(rect.xMin, rect.yMax, 0), new Vector3(rect.xMax, rect.yMax, 0));
        // Handles.EndGUI();
        // ..
        GUILayout.EndHorizontal(); // End the horizontal layout
        EditorGUILayout.Space();
    }
    private async void RefreshFineTunedModels(bool silent = true)
    {
        if (System.String.IsNullOrEmpty(this.openAIAPIKey))
        {
            if (silent == false) UnityTools.ShowMessage("You must setup a valid OpenAI secret project api key.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
            return;
        }
        if (silent == false) UnityTools.ReportProgress(1, "AI code assistant is fetching fine tuned models...");
        try
        {
            string result = await AI_CodeAssistant.GetSafeWebRequestAsync(AI_CodeAssistant.MODEL_ENDPOINT, this.openAIAPIKey, this.openAIORGKey, this.openAIProject);
            if (!System.String.IsNullOrEmpty(result))
            {
                OpenAIFineTuneResponse response = JsonUtility.FromJson<OpenAIFineTuneResponse>(result);
                List<string> models = new List<string>();
                models.Add(AI_CodeAssistant.GPT4O_MINI);
                foreach (var fineTune in response.data)
                {
                    string model = fineTune.id ?? null;
                    if (!String.IsNullOrEmpty(model) && model.StartsWith("ft:", StringComparison.OrdinalIgnoreCase))
                    {
                        models.Add(model);
                    }
                }
                AI_CodeAssistant.FineTunedModels = models.ToArray();
                AI_CodeAssistant.FineTunedModelsLoaded = true;
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogException(e);
        }
        finally 
        {
            // TODO: Update List
            UpdateSelectedModelIndex();
            EditorUtility.ClearProgressBar();
        }
    }
    private void UpdateSelectedModelIndex()
    {
        this.selectedIndex = 0;
        if (!String.IsNullOrEmpty(this.fineTunedModel) && AI_CodeAssistant.FineTunedModels != null && AI_CodeAssistant.FineTunedModels.Length > 0)
        {
            for (int i = 0; i < AI_CodeAssistant.FineTunedModels.Length; i++)
            {
                string checkModelName = AI_CodeAssistant.FineTunedModels[i];
                if (checkModelName.Equals(this.fineTunedModel, StringComparison.OrdinalIgnoreCase)) 
                {
                    this.selectedIndex = i;
                }                    
            }
        }
    }
    public static void UpdateAICodeSettings(OpenAILogicModel logicModel, OpenAISourceControl sourceControl, float temperature, int maxtokens, float topp, float ppenalty, float fpenalty, string persona, bool copilot)
    {
        if (!Directory.Exists(CanvasToolsExporter.SettingsFolder))
        {
            try { System.IO.Directory.CreateDirectory(CanvasToolsExporter.SettingsFolder); } catch { }
        }
        if (!File.Exists(CanvasToolsExporter.SettingsJson))
        {
            try { CanvasToolsExporter.WriteWorkspaceSettings(); } catch { }
            return;
        }
        try
        {
            // Read existing settings
            string json = File.ReadAllText(CanvasToolsExporter.SettingsJson);
            Newtonsoft.Json.Linq.JObject settings = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (settings == null) settings = Newtonsoft.Json.Linq.JObject.Parse("{}");
            // Update or add vscode-openai settings
            string developerPersona = !String.IsNullOrEmpty(persona) ? persona : AI_CodeAssistant.DEFAULT_DEVELOPER_PERSONA;
            string defaultModel = !String.IsNullOrEmpty(CanvasToolsExporter.DefaultFineTunedModel) ? CanvasToolsExporter.DefaultFineTunedModel : AI_CodeAssistant.GPT4O_MINI;
            string scmModel = !String.IsNullOrEmpty(CanvasToolsExporter.SourceControlFineTunedModel) ? CanvasToolsExporter.SourceControlFineTunedModel : AI_CodeAssistant.GPT4O_MINI;
            settings["vscode-openai.defaultModel"] = defaultModel;
            if (logicModel == OpenAILogicModel.gptX && sourceControl == OpenAISourceControl.SelectedModel)
            {
                settings["vscode-openai.scmModel"] = scmModel;
            }
            else
            {
                try { settings.Remove("vscode-openai.scmModel"); }catch{}
            }
            settings["vscode-openai.editor.enabled"] = !copilot;
            settings["vscode-openai.prompts.persona.developer"] = developerPersona;
            settings["vscode-openai.conversation-configuration.max-tokens"] = maxtokens;
            settings["vscode-openai.conversation-configuration.temperature"] = temperature;
            settings["vscode-openai.conversation-configuration.presence-penalty"] = ppenalty;
            settings["vscode-openai.conversation-configuration.frequency-penalty"] = fpenalty;
            settings["vscode-openai.conversation-configuration.top-p"] = topp;
            settings["github.copilot.editor.enableAutoCompletions"] = copilot;
            // Write updated json settings
            string updatedJson = settings.ToString(Formatting.Indented);
            File.WriteAllText(CanvasToolsExporter.SettingsJson, updatedJson);
            UnityEngine.Debug.Log("AI code settings updated successfully.");
        }
        catch (System.Exception e3)
        {
            UnityEngine.Debug.LogException(e3);
        }
    }

    public static void UpdateAIConvertSettings(float temperature, int maxtokens)
    {
        if (!Directory.Exists(CanvasToolsExporter.SettingsFolder))
        {
            try { System.IO.Directory.CreateDirectory(CanvasToolsExporter.SettingsFolder); } catch { }
        }
        if (!File.Exists(CanvasToolsExporter.SettingsJson))
        {
            try { CanvasToolsExporter.WriteWorkspaceSettings(); } catch { }
            return;
        }
        try
        {
            // Read existing settings
            string json = File.ReadAllText(CanvasToolsExporter.SettingsJson);
            Newtonsoft.Json.Linq.JObject settings = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (settings == null) settings = Newtonsoft.Json.Linq.JObject.Parse("{}");
            // Write updated json settings
            string updatedJson = settings.ToString(Formatting.Indented);
            File.WriteAllText(CanvasToolsExporter.SettingsJson, updatedJson);
            UnityEngine.Debug.Log("AI convert settings updated successfully.");
        }
        catch (System.Exception e3)
        {
            UnityEngine.Debug.LogException(e3);
        }
    }

    private void DeleteTrainingDataset()
    {
        if (System.String.IsNullOrEmpty(this.fineTunedModel))
        {
            UnityTools.ShowMessage("You must enter a valid model indentifier.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
            return;
        }
        if (System.String.IsNullOrEmpty(this.openAIAPIKey))
        {
            UnityTools.ShowMessage("You must setup a valid OpenAI secret project api key.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
            return;
        }
        if (UnityTools.ShowMessage("Are you sure you want to DELETE the fine tuned model?", CanvasToolsStatics.CANVAS_TOOLS_TITLE, "Yes", "No"))
        {
            RemoveFineTunedModel();
        }
    }
    private async void RemoveFineTunedModel()
    {
        this.fineTunedModel = AI_CodeAssistant.FineTunedModels[this.selectedIndex];
        UnityTools.ReportProgress(1, "AI code assistant is removing the fine tuned model...");
        try
        {
            await AI_CodeAssistant.DeleteWebRequestAsync(AI_CodeAssistant.MODEL_ENDPOINT, this.openAIAPIKey, this.openAIORGKey, this.openAIProject, this.fineTunedModel);
            RefreshFineTunedModels();
            UnityTools.ShowMessage("AI training dataset removed successfully.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogException(e);
        }
        finally 
        {
            this.fineTunedModel = AI_CodeAssistant.GPT4O_MINI;
            SaveAssistantProperties();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
        }
    }
    // private void FetchTrainingDataset()
    // {
    //     if (UnityTools.ShowMessage("This will overwrite the existing training dataset. Do you want to continue?", CanvasToolsStatics.CANVAS_TOOLS_TITLE, "Yes", "No"))
    //     {
    //         FetchJsonlFile();
    //     }
    // }
    // private const string jsonlFileUrl = "https://raw.githubusercontent.com/babylontoolkit/AICodeAssistant/main/Training/codewrx.ai.jsonl";
    // private const string savePath = "Assets/[Training]/codewrx.ai.jsonl";
    // private async void FetchJsonlFile()
    // {
    //     UnityTools.ReportProgress(1, "AI code assistant is fetching the training data...");
    //     string fixedPath = Path.GetFullPath(savePath);
    //     string parentFolder = Path.GetDirectoryName(fixedPath);
    //     if (!Directory.Exists(parentFolder))
    //     {
    //         Directory.CreateDirectory(parentFolder);
    //     }
    //     if (File.Exists(fixedPath))
    //     {
    //         try { File.Delete(fixedPath); } catch{}
    //     }
    //     try
    //     {
    //         await AI_CodeAssistant.DownloadFileAsync(jsonlFileUrl, fixedPath);
    //     }
    //     catch (System.Exception e)
    //     {
    //         UnityEngine.Debug.LogException(e);
    //     }
    //     finally 
    //     {
    //         AssetDatabase.Refresh();
    //         EditorUtility.ClearProgressBar();
    //     }
    //     if (File.Exists(fixedPath))
    //     {
    //         UnityTools.ShowMessage("AI training dataset fetched successfully.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
    //     }
    //     else
    //     {
    //         UnityTools.ShowMessage("Failed to fetch training dataset.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
    //     }
    // }
}
// ..
// OpenAI API Logic Models
// ..
[System.Serializable]
public enum OpenAILogicModel
{
    [InspectorName("GPT-3.5 Turbo")]
    gpt35_turbo = 0,
    [InspectorName("GPT-4o Mini")]
    gpt4o_mini = 1,
    [InspectorName("GPT-4o")]
    gpt4o = 2,
    [InspectorName("O1-Preview")]
    o1preview = 3,
    [InspectorName("O1-Preview Mini")]
    o1preview_mini = 4,
    [InspectorName("Fine Tuned Model")]
    gptX = 5
}
// ..
// Github Copilot AI
// ..
// [System.Serializable]
// public enum OpenAICompletions
// {
//     CodeAssistant = 0,
//     GithubCopilot = 1
// }
// ..
// OpenAI API Pseudo Code
// ..
[System.Serializable]
public enum OpenAIPseudoCode
{
    LiveScript = 0,
    ReferenceText = 1
}
// ..
// OpenAI API Pseudo Code
// ..
[System.Serializable]
public enum OpenAISourceControl
{
    DefaultModel = 0,
    SelectedModel = 1,
    GithubCopilot = 2
}
// ..
// Payload structure for the OpenAI API request
// ..
[System.Serializable]
public class OpenAIRequestPayload
{
    public string model;
    public List<Message> messages;
    public int n;
    public float temperature;
    public int? max_completion_tokens;
    public float frequency_penalty;
    public float presence_penalty;
    public float top_p;
}
[System.Serializable]
public class Message
{
    public string role;
    public string content;
}
// ..
// Structure for OpenAI API response
// ..
[System.Serializable]
public class OpenAIResponse
{
    public List<Choice> choices;
}
[System.Serializable]
public class Choice
{
    public Message message;
}
[Serializable]
public class OpenAIFineTuneResponse
{
    public List<FineTuneModelData> data;
}
[Serializable]
public class FineTuneModelData
{
    public string id;
}
#endif