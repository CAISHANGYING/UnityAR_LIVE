using UnityEngine;
using UnityEngine.Experimental.Rendering;
#if UNITY_2022_2_OR_NEWER
using UnityEngine.Rendering;
#endif

public static class TextureUtils
{
    public static Texture2D Rotate90DegreesCCW(Texture2D original)
    {
        if (original == null) return null;

        Color32[] originalPixels = original.GetPixels32();
        int w = original.width;
        int h = original.height;
        Color32[] rotatedPixels = new Color32[originalPixels.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                rotatedPixels[y + (w - 1 - x) * h] = originalPixels[x + y * w];
            }
        }

        Texture2D rotated = new Texture2D(h, w, original.format, false);
        rotated.SetPixels32(rotatedPixels);
        rotated.Apply();
        return rotated;
    }


    public static Texture2D RotateTexture(Texture2D originalTexture, NativeGallery.ImageOrientation orientation)
    {
        if (orientation == NativeGallery.ImageOrientation.Normal)
        {
            var newTex = new Texture2D(originalTexture.width, originalTexture.height, originalTexture.format, false);
            newTex.SetPixels32(originalTexture.GetPixels32());
            newTex.Apply();
            return newTex;
        }

        int width = originalTexture.width;
        int height = originalTexture.height;
        float angle = 0;
        Vector3 scale = Vector3.one;

        switch (orientation)
        {
            case NativeGallery.ImageOrientation.Rotate90: angle = -90; break;
            case NativeGallery.ImageOrientation.Rotate180: angle = 180; break;
            case NativeGallery.ImageOrientation.Rotate270: angle = -270; break;
            case NativeGallery.ImageOrientation.FlipHorizontal: scale.x = -1; break;
            case NativeGallery.ImageOrientation.Transpose: angle = -90; scale.x = -1; break;
            case NativeGallery.ImageOrientation.FlipVertical: scale.y = -1; break;
            case NativeGallery.ImageOrientation.Transverse: angle = 90; scale.x = -1; break;
        }

        bool swapDimensions = angle == -90 || angle == -270 || angle == 90;
        int newWidth = swapDimensions ? height : width;
        int newHeight = swapDimensions ? width : height;

        var safeFormat = GraphicsFormat.R8G8B8A8_UNorm;
        var descriptor = new RenderTextureDescriptor(newWidth, newHeight, safeFormat, 0);
        RenderTexture rt = RenderTexture.GetTemporary(descriptor);
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, newWidth, 0, newHeight);
        GL.MultMatrix(Matrix4x4.TRS(new Vector3(newWidth / 2f, newHeight / 2f, 0), Quaternion.Euler(0, 0, angle), scale));
        Graphics.DrawTexture(new Rect(-width / 2f, -height / 2f, width, height), originalTexture);
        GL.PopMatrix();

        RenderTexture flippedRT = RenderTexture.GetTemporary(descriptor);
        Graphics.Blit(rt, flippedRT, new Vector2(1, -1), new Vector2(0, 1));
        RenderTexture.ReleaseTemporary(rt);
        rt = flippedRT;

        Texture2D newTexture = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
        newTexture.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        newTexture.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return newTexture;
    }
}