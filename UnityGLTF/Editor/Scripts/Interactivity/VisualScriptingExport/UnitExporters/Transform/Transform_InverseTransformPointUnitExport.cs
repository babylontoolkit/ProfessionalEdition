using System;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityGLTF.Interactivity.Export;
using UnityGLTF.Interactivity.Schema;

namespace UnityGLTF.Interactivity.VisualScripting.Export
{
    public class Transform_InverseTransformPointUnitExport : IUnitExporter
    {
        public Type unitType { get => typeof(InvokeMember); }
        
        [InitializeOnLoadMethod]
        private static void Register()
        {
            InvokeUnitExport.RegisterInvokeExporter(typeof(Transform), nameof(Transform.InverseTransformPoint), new Transform_InverseTransformPointUnitExport());
        }
        
        public bool InitializeInteractivityNodes(UnitExporter unitExporter)
        {
            var invokeUnit = unitExporter.unit as InvokeMember;
            
            TransformHelpers.GetLocalPointFromWorldPoint(unitExporter, out var targetRef, out var worldPointSocket, out var localPoint);

            targetRef.MapToInputPort(invokeUnit.target);

            if (invokeUnit.valueInputs.Count > 2)
            {
                var combine = unitExporter.CreateNode<Math_Combine3Node>();
                combine.ValueIn(Math_Combine3Node.IdValueA).MapToInputPort(invokeUnit.valueInputs["%x"]);
                combine.ValueIn(Math_Combine3Node.IdValueB).MapToInputPort(invokeUnit.valueInputs["%y"]);
                combine.ValueIn(Math_Combine3Node.IdValueC).MapToInputPort(invokeUnit.valueInputs["%z"]);
                worldPointSocket.ConnectToSource(combine.FirstValueOut());
            }
            else
                worldPointSocket.MapToInputPort(invokeUnit.valueInputs["%position"]);

            localPoint.MapToPort(invokeUnit.result);
            
            unitExporter.ByPassFlow(invokeUnit.enter, invokeUnit.exit);

            return true;
        }
    }
}