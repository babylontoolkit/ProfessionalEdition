#if UNITY_EDITOR && BT_DRACO_UNITY
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CanvasTools;
using UnityEditor;

/// <summary>
/// Draco encoder over Draco for Unity's native draco_unity plugin (draco-mesh-compression plan D1). It passes flip = false
/// everywhere, because the exporter's data is already in final glTF space. Registered as Core's
/// <see cref="DracoMeshEncoder.NativeEncode"/> when this optional assembly compiles, which it only does with Draco for Unity 5.x.
/// </summary>
[InitializeOnLoad]
public static class DracoNativeEncoder
{
	/// <summary>The Editor native plugin shipped by com.unity.cloud.draco 5.x.</summary>
	private const string NativeLibrary = "draco_unity";

	/// <summary>Registers this encoder as Core's Draco hook.</summary>
	static DracoNativeEncoder()
	{
		DracoMeshEncoder.NativeEncode = Encode;
	}

	/// <summary>
	/// Encodes one request into a Draco bitstream. Pins the managed arrays only for the native calls and always
	/// releases the native encoder.
	/// </summary>
	/// <param name="request">Vertices, indices and settings, already in glTF space.</param>
	/// <returns>The bitstream, the decoded counts and the Draco id of each semantic.</returns>
	/// <exception cref="InvalidOperationException">The native plugin refused the indices or failed to encode.</exception>
	public static DracoEncodeResult Encode(DracoEncodeRequest request)
	{
		IntPtr encoder = dracoEncoderCreate(request.VertexCount);
		if (encoder == IntPtr.Zero) throw new InvalidOperationException("dracoEncoderCreate returned no encoder");
		List<GCHandle> pinnedArrays = new List<GCHandle>();
		try
		{
			dracoEncoderSetCompressionSpeed(encoder, request.EncodingSpeed, request.DecodingSpeed);
			DracoQuantization quantization = request.Quantization;
			dracoEncoderSetQuantizationBits(encoder, quantization.PositionBits, quantization.NormalBits, quantization.TexCoordBits, quantization.ColorBits, quantization.GenericBits);
			IntPtr indices = Pin(request.Indices, pinnedArrays);
			if (dracoEncoderSetIndices(encoder, (int)DracoDataType.UInt32, (uint)request.Indices.Length, false, indices) == false)
			{
				throw new InvalidOperationException("dracoEncoderSetIndices failed");
			}
			DracoEncodeResult result = new DracoEncodeResult();
			foreach (DracoAttributeInput attribute in request.Attributes)
			{
				IntPtr data = Pin(attribute.Data, pinnedArrays);
				uint attributeId = dracoEncoderSetAttribute(encoder, (int)attribute.AttributeType, (int)attribute.DataType, attribute.ComponentCount, attribute.ByteStride, false, data);
				result.AttributeIds[attribute.Semantic] = (int)attributeId;
			}
			if (dracoEncoderEncode(encoder, request.PreserveOrder) == false) throw new InvalidOperationException("dracoEncoderEncode failed");
			dracoEncoderGetEncodeBuffer(encoder, out IntPtr encodedData, out ulong encodedSize);
			result.Data = new byte[(int)encodedSize];
			Marshal.Copy(encodedData, result.Data, 0, (int)encodedSize);
			result.VertexCount = (int)dracoEncoderGetEncodedVertexCount(encoder);
			result.IndexCount = (int)dracoEncoderGetEncodedIndexCount(encoder);
			return result;
		}
		finally
		{
			foreach (GCHandle pinnedArray in pinnedArrays) pinnedArray.Free();
			dracoEncoderRelease(encoder);
		}
	}

	/// <summary>Pins a managed array for the duration of one encode and returns its address.</summary>
	/// <param name="array">The managed array the native call reads.</param>
	/// <param name="pinnedArrays">Collects the handle so the caller frees it after the encode.</param>
	/// <returns>The address of the array's first element.</returns>
	private static IntPtr Pin(Array array, List<GCHandle> pinnedArrays)
	{
		GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
		pinnedArrays.Add(handle);
		return handle.AddrOfPinnedObject();
	}

	[DllImport(NativeLibrary)] private static extern IntPtr dracoEncoderCreate(int vertexCount);
	[DllImport(NativeLibrary)] private static extern void dracoEncoderRelease(IntPtr encoder);
	[DllImport(NativeLibrary)] private static extern void dracoEncoderSetCompressionSpeed(IntPtr encoder, int encodingSpeed, int decodingSpeed);
	[DllImport(NativeLibrary)] private static extern void dracoEncoderSetQuantizationBits(IntPtr encoder, int position, int normal, int uv, int color, int generic);
	[DllImport(NativeLibrary)] [return: MarshalAs(UnmanagedType.I1)]
	private static extern bool dracoEncoderEncode(IntPtr encoder, [MarshalAs(UnmanagedType.I1)] bool preserveTriangleOrder);
	[DllImport(NativeLibrary)] private static extern uint dracoEncoderGetEncodedVertexCount(IntPtr encoder);
	[DllImport(NativeLibrary)] private static extern uint dracoEncoderGetEncodedIndexCount(IntPtr encoder);
	[DllImport(NativeLibrary)] private static extern void dracoEncoderGetEncodeBuffer(IntPtr encoder, out IntPtr data, out ulong size);
	[DllImport(NativeLibrary)] [return: MarshalAs(UnmanagedType.I1)]
	private static extern bool dracoEncoderSetIndices(IntPtr encoder, int indexComponentType, uint indexCount, [MarshalAs(UnmanagedType.I1)] bool flip, IntPtr indices);
	[DllImport(NativeLibrary)]
	private static extern uint dracoEncoderSetAttribute(IntPtr encoder, int attributeType, int dracoDataType, int componentCount, int stride, [MarshalAs(UnmanagedType.I1)] bool flip, IntPtr data);
}
#endif
