#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

using UnityEditor;
using UnityEngine;

public class AI_ScriptConverter : EditorWindow
{
    private string outputPrefix = null;
    private string scriptContent = null;
    private string openAIAPIKey = null;
    private string openAIORGKey = AI_CodeAssistant.DEFAULT_ORGANIZATION_ID;
    private string openAIProject = AI_CodeAssistant.DEFAULT_PROJECT_NAME;
    private int maxTokens = 0;
    private float temperature = 0.1f;
    private float topp = 1.0f;
    private float presencePenalty = 0.0f;
    private float frequencyPenalty = 0.0f;
    private string fineTunedModel = AI_CodeAssistant.GPT4O_MINI;
    private OpenAILogicModel logicModel = OpenAILogicModel.gpt4o_mini;
    private MonoScript scriptAsset = null;
    private TextAsset promptAsset = null;
    private OpenAIPseudoCode pseudoCode = OpenAIPseudoCode.LiveScript;
    private string promptPath = null;
    private bool isCustomPrompt = false;
    private bool isUsingCustomPrompt = false;

    public void OnInitialize(string scriptFilename, string scriptContent, MonoScript scriptComponent)
    {
        maxSize = new Vector2(604, 140);
        minSize = this.maxSize;
        string fileName = Path.GetFileNameWithoutExtension(scriptFilename);
        this.scriptAsset = scriptComponent;
        this.scriptContent = scriptContent;
        this.outputPrefix = Path.Combine(Directory.GetParent(scriptFilename).FullName, fileName);
        this.pseudoCode = AI_CodeAssistant.ReadOpenAIPseudoCode();
        this.logicModel = AI_CodeAssistant.ReadOpenAIModel();
        this.promptPath = AI_CodeAssistant.ReadOpenAIPrompt();
        this.maxTokens = AI_CodeAssistant.ReadOpenAIMaxTokens();
        this.temperature = AI_CodeAssistant.ReadOpenAITemperature();
        this.topp = AI_CodeAssistant.ReadOpenAITopp();
        this.presencePenalty = AI_CodeAssistant.ReadOpenAIPPenalty();
        this.frequencyPenalty = AI_CodeAssistant.ReadOpenAIFPenalty();
        this.openAIAPIKey = AI_CodeAssistant.ReadOpenAIAPIKey();
        this.openAIORGKey = AI_CodeAssistant.ReadOpenAIORGKey();
        this.openAIProject = AI_CodeAssistant.ReadOpenAIProject();
        this.fineTunedModel = AI_CodeAssistant.ReadOpenAIModelName();
        if (String.IsNullOrEmpty(this.fineTunedModel))
        {
            this.fineTunedModel = AI_CodeAssistant.GPT4O_MINI;
        }
        // ..
        // DEBUG: UnityEngine.Debug.LogFormat("DEBUG: Convert script component initialized - script: {0}", scriptFilename);
        // DEBUG: UnityEngine.Debug.LogFormat("DEBUG: Convert script component initialized - output: {0}", this.outputPrefix);
        // ..
        string customFilename = Path.Combine(Directory.GetParent(scriptFilename).FullName, fileName + ".txt");
        if (!String.IsNullOrEmpty(customFilename) && File.Exists(customFilename))
        {
            this.promptPath = AI_CodeAssistant.GetAssetPathFromFullPath(customFilename);
            this.isCustomPrompt = true;
            this.isUsingCustomPrompt = false;
        }
        else
        {
            this.isCustomPrompt = false;
            this.isUsingCustomPrompt = false;
        }
    }

    void OnEnable()
    {
        titleContent = new GUIContent("AI Code Assistant Converter");
    }

    public void OnGUI()
    {
        EditorGUI.BeginDisabledGroup(true);
            if (logicModel == OpenAILogicModel.gptX)
            {
                fineTunedModel = EditorGUILayout.TextField("Open AI Model:", fineTunedModel, GUILayout.ExpandWidth(true));
                EditorGUILayout.Space();
            }
            else
            {
                logicModel = (OpenAILogicModel)EditorGUILayout.EnumPopup("Open AI Model:", (OpenAILogicModel)logicModel, GUILayout.ExpandWidth(true));
                EditorGUILayout.Space();
            }
            scriptAsset = (MonoScript)EditorGUILayout.ObjectField("Script To Convert:", scriptAsset, typeof(MonoScript), false);
            EditorGUILayout.Space();
        EditorGUI.EndDisabledGroup();

        promptAsset = (TextAsset)EditorGUILayout.ObjectField("Prompt Template:", promptAsset, typeof(TextAsset), false);
        EditorGUILayout.Space();

        pseudoCode = (OpenAIPseudoCode)EditorGUILayout.EnumPopup("Pseudo Code Type:", (OpenAIPseudoCode)pseudoCode, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Prompt Template"))
        {
            if (UnityTools.ShowMessage("Are you sure want to reset the prompt template?", CanvasToolsStatics.CANVAS_TOOLS_TITLE, "Yes", "No"))
            {
                ResetDefaultPrompt();
            }
        }
        if (GUILayout.Button("Generate Script Component"))
        {
            ConvertScriptComponent();
        }
        EditorGUILayout.EndHorizontal();
    }

    public void OnInspectorUpdate()
    {
        string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        string folderPath = System.IO.Path.GetDirectoryName(scriptPath);
        if (promptAsset == null)
        {
            string promptFile = (logicModel == OpenAILogicModel.gptX && fineTunedModel.StartsWith("ft:", StringComparison.OrdinalIgnoreCase)) ? "DefaultFineTunedModel.txt" : "DefaultScriptConverter.txt";
            string defaultPath = (folderPath + "/" + promptFile);
            // DEPRECATED: string templatePath = !System.String.IsNullOrEmpty(this.promptPath) ? this.promptPath : defaultPath;
            string templatePath = defaultPath;
            if (!System.String.IsNullOrEmpty(this.promptPath))
            {
                templatePath = this.promptPath;
                if (this.isCustomPrompt == true)
                {
                    this.isUsingCustomPrompt = true;
                }
            }
            try
            {
                promptAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(templatePath);
            }
            catch /*(Exception e)*/
            {
                // DEBUG: UnityEngine.Debug.LogErrorFormat("DEBUG: Load Prompt Asset At Path Error: {0}", e.Message);
            }
            // ..
            // Double Check Has Valid Prompt Asset
            // ..
            if (promptAsset == null)
            {
                try
                {
                    promptAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(defaultPath);
                }
                catch /*(Exception e)*/
                {
                    // DEBUG: UnityEngine.Debug.LogErrorFormat("DEBUG: Load Prompt Asset At Path Error: {0}", e.Message);
                }
            }
            // ..
            // Mark the scene as dirty so the change is saved
            // ..
            if (promptAsset != null)
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

    private void ResetDefaultPrompt()
    {
        this.promptPath = null;
        this.promptAsset = null;
        AI_CodeAssistant.DeleteOpenAIPrompt();
    }

    private void ConvertScriptComponent()
    {
        if (System.String.IsNullOrEmpty(this.openAIAPIKey))
        {
            UnityTools.ShowMessage("You must setup a valid OpenAI secret project api key in the Code Assistant settings", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
            return;
        }
        if (System.String.IsNullOrEmpty(this.scriptContent))
        {
            UnityTools.ShowMessage("You must specify a valid script component to convert.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
            return;
        }
        if (this.promptAsset == null || System.String.IsNullOrEmpty(this.promptAsset.text))
        {
            UnityTools.ShowMessage("You must specify a valid prompt template to convert.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
            return;
        }
        string outputFilename = this.outputPrefix;
        if (this.pseudoCode == OpenAIPseudoCode.LiveScript)
        {
            outputFilename += ".ts";
        }
        else
        {
            outputFilename += ".txt";
        }
        if (File.Exists(outputFilename))
        {
            if (!UnityTools.ShowMessage("Are you sure you want to overwrite existing output file?", CanvasToolsStatics.CANVAS_TOOLS_TITLE, "Yes", "No"))
            {
                return;
            }                    
        }
        UnityTools.ReportProgress(1, "AI code assistant is generating pseudo script component...");
        if (this.isUsingCustomPrompt == false) AI_CodeAssistant.SaveOpenAIPrompt(AssetDatabase.GetAssetPath(this.promptAsset));
        AI_CodeAssistant.SaveOpenAIPseudoCode(this.pseudoCode);
        // ..
        // OpenAI Request Payload
        // ..
        string aiLogicModel = AI_CodeAssistant.GetLogicModelName(this.logicModel, this.fineTunedModel);
        string standardPrompt = AI_CodeAssistant.DEFAULT_SCRIPT_CONVERTER;
        string fineTunedPrompt = AI_CodeAssistant.DEFAULT_FINE_TUNED_MODEL;
        string defaultPrompt = (logicModel == OpenAILogicModel.gptX && fineTunedModel.StartsWith("ft:", StringComparison.OrdinalIgnoreCase)) ? fineTunedPrompt : standardPrompt;
        string promptContent = (promptAsset != null && !String.IsNullOrEmpty(promptAsset.text)) ? promptAsset.text : defaultPrompt;
        string scriptContent = ("\\n\\n" + AI_CodeAssistant.EscapeDefaultJsonString(this.scriptContent));
        float maxTemperature = this.temperature;
        int? maxCompletionTokens = this.maxTokens;
        float topp = this.topp;
        float frequencyPenalty = this.frequencyPenalty;
        float presencePenalty = this.presencePenalty;
        string requestPayload = JsonUtility.ToJson(new OpenAIRequestPayload()
        {
            model = aiLogicModel,
            messages = new List<Message>()
            {
                new Message { role = "system", content = promptContent },
                new Message { role = "user", content = scriptContent }
            },
            n = 1,
            temperature = maxTemperature,
            max_completion_tokens = maxCompletionTokens.Value > 0 ? maxCompletionTokens.Value : null,
            frequency_penalty = frequencyPenalty,
            presence_penalty = presencePenalty,
            top_p = topp,
        });
        PostConvertWebRequest(requestPayload, outputFilename);
    }

    private async void PostConvertWebRequest(string payload, string output)
    {
        int code = 0;
        string message = "Unknown";
        try
        {
            string result = await AI_CodeAssistant.PostSafeWebRequestAsync(AI_CodeAssistant.API_ENDPOINT, this.openAIAPIKey, this.openAIORGKey, this.openAIProject, payload);
            if (!System.String.IsNullOrEmpty(result))
            {
                UnityEngine.Debug.LogFormat("AI Code Assistant Response: {0}", result); // Note: Always Log Response
                var openAIResponse = JsonUtility.FromJson<OpenAIResponse>(result);
                if (openAIResponse != null && openAIResponse.choices.Count > 0)
                {
                    if (openAIResponse.choices.Count > 1)
                    {
                        for (int i = 0; i < openAIResponse.choices.Count; i++)
                        {
                            string outputContent = openAIResponse.choices[i].message.content;
                            string outputExtension = (this.pseudoCode == OpenAIPseudoCode.LiveScript) ? ".ts" : ".txt";
                            string indexedFilename = Path.Combine(Directory.GetParent(output).FullName, Path.GetFileNameWithoutExtension(output) + "." + (i + 1).ToString() + outputExtension);
                            File.WriteAllText(indexedFilename, outputContent, System.Text.Encoding.UTF8);
                        }
                    }
                    else
                    {
                        string outputContent = openAIResponse.choices[0].message.content;
                        File.WriteAllText(output, outputContent, System.Text.Encoding.UTF8);
                    }
                    AssetDatabase.Refresh();
                    code = (int)HttpStatusCode.OK;
                    message = System.String.Format("{0} - {1}", code, "OK");
                }
                else
                {
                    code = (int)HttpStatusCode.InternalServerError;
                    message = System.String.Format("{0} - {1}", code, "Null Conversion Data");
                }
            }
            else
            {
                code = (int)HttpStatusCode.InternalServerError;
                message = System.String.Format("{0} - {1}", code, "Null Web Response");
            }
        }
        catch (Exception ex)
        {
            code = 0;
            message = ex.Message;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (code == 200)
            {
                UnityTools.ShowMessage("AI script conversion complete.", CanvasToolsStatics.CANVAS_TOOLS_TITLE);
                this.Close();
            }
            else
            {
                UnityEngine.Debug.LogError(message); 
                UnityTools.ShowMessage(message, CanvasToolsStatics.CANVAS_TOOLS_TITLE);
            }
        }
    }
}
#endif