using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    [CustomEditorForRenderPipeline(typeof(Light), typeof(CustomRenderPipelineAsset))]
    public class CustomLightEditor : LightEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var type = (LightType)settings.lightType.enumValueIndex;
            if (type == LightType.Spot)
            {
                EditorGUILayout.PropertyField(settings.lightShape);
                settings.DrawInnerAndOuterSpotAngle();
            }

            serializedObject.ApplyModifiedProperties();
            base.OnInspectorGUI();
        }

        //copy of CoreLightEditorUtilities
        static float SliderLineHandle(Vector3 position, Vector3 direction, float value)
        {
            return SliderLineHandle(GUIUtility.GetControlID(FocusType.Passive), position, direction, value, "");
        }


        //copy of CoreLightEditorUtilities
        static void DrawHandleLabel(Vector3 handlePosition, string labelText, float offsetFromHandle = 0.3f)
        {
            Vector3 labelPosition = Vector3.zero;

            var style = new GUIStyle { normal = { background = Texture2D.whiteTexture } };
            GUI.color = new Color(0.82f, 0.82f, 0.82f, 1);

            labelPosition = handlePosition + Handles.inverseMatrix.MultiplyVector(Vector3.up) * HandleUtility.GetHandleSize(handlePosition) * offsetFromHandle;
            Handles.Label(labelPosition, labelText, style);
        }

        //copy of CoreLightEditorUtilities
        static float SliderLineHandle(int id, Vector3 position, Vector3 direction, float value, string labelText = "")
        {
            Vector3 pos = position + direction * value;
            float sizeHandle = HandleUtility.GetHandleSize(pos);
            bool temp = GUI.changed;
            GUI.changed = false;
            pos = Handles.Slider(id, pos, direction, sizeHandle * 0.03f, Handles.DotHandleCap, 0f);
            if (GUI.changed)
            {
                value = Vector3.Dot(pos - position, direction);
            }

            GUI.changed |= temp;

            if (GUIUtility.hotControl == id && !String.IsNullOrEmpty(labelText))
            {
                labelText += FormattableString.Invariant($"{value:0.00}");
                DrawHandleLabel(pos, labelText);
            }

            return value;
        }

        //TODO: decompose arguments (or tuples) + put back to CoreLightEditorUtilities
        static void DrawOrthoFrustumWireframe(Vector4 widthHeightMaxRangeMinRange, float distanceTruncPlane = 0f)
        {
            float halfWidth = widthHeightMaxRangeMinRange.x * 0.5f;
            float halfHeight = widthHeightMaxRangeMinRange.y * 0.5f;
            float maxRange = widthHeightMaxRangeMinRange.z;
            float minRange = widthHeightMaxRangeMinRange.w;

            Vector3 sizeX = new Vector3(halfWidth, 0, 0);
            Vector3 sizeY = new Vector3(0, halfHeight, 0);
            Vector3 nearEnd = new Vector3(0, 0, minRange);
            Vector3 farEnd = new Vector3(0, 0, maxRange);

            Vector3 s1 = nearEnd + sizeX + sizeY;
            Vector3 s2 = nearEnd - sizeX + sizeY;
            Vector3 s3 = nearEnd - sizeX - sizeY;
            Vector3 s4 = nearEnd + sizeX - sizeY;

            Vector3 e1 = farEnd + sizeX + sizeY;
            Vector3 e2 = farEnd - sizeX + sizeY;
            Vector3 e3 = farEnd - sizeX - sizeY;
            Vector3 e4 = farEnd + sizeX - sizeY;

            Handles.DrawLine(s1, s2);
            Handles.DrawLine(s2, s3);
            Handles.DrawLine(s3, s4);
            Handles.DrawLine(s4, s1);

            Handles.DrawLine(e1, e2);
            Handles.DrawLine(e2, e3);
            Handles.DrawLine(e3, e4);
            Handles.DrawLine(e4, e1);

            Handles.DrawLine(s1, e1);
            Handles.DrawLine(s2, e2);
            Handles.DrawLine(s3, e3);
            Handles.DrawLine(s4, e4);

            if (distanceTruncPlane > 0f)
            {
                Vector3 truncPoint = new Vector3(0, 0, distanceTruncPlane);
                Vector3 t1 = truncPoint + sizeX + sizeY;
                Vector3 t2 = truncPoint - sizeX + sizeY;
                Vector3 t3 = truncPoint - sizeX - sizeY;
                Vector3 t4 = truncPoint + sizeX - sizeY;

                Handles.DrawLine(t1, t2);
                Handles.DrawLine(t2, t3);
                Handles.DrawLine(t3, t4);
                Handles.DrawLine(t4, t1);
            }
        }

        //TODO: decompose arguments (or tuples) + put back to CoreLightEditorUtilities
        static Vector4 DrawOrthoFrustumHandle(Vector4 widthHeightMaxRangeMinRange, bool useNearHandle)
        {
            float halfWidth = widthHeightMaxRangeMinRange.x * 0.5f;
            float halfHeight = widthHeightMaxRangeMinRange.y * 0.5f;
            float maxRange = widthHeightMaxRangeMinRange.z;
            float minRange = widthHeightMaxRangeMinRange.w;
            Vector3 farEnd = new Vector3(0, 0, maxRange);

            if (useNearHandle)
            {
                minRange = SliderLineHandle(Vector3.zero, Vector3.forward, minRange);
            }

            maxRange = SliderLineHandle(Vector3.zero, Vector3.forward, maxRange);

            EditorGUI.BeginChangeCheck();
            halfWidth = SliderLineHandle(farEnd, Vector3.right, halfWidth);
            halfWidth = SliderLineHandle(farEnd, Vector3.left, halfWidth);
            if (EditorGUI.EndChangeCheck())
            {
                halfWidth = Mathf.Max(0f, halfWidth);
            }

            EditorGUI.BeginChangeCheck();
            halfHeight = SliderLineHandle(farEnd, Vector3.up, halfHeight);
            halfHeight = SliderLineHandle(farEnd, Vector3.down, halfHeight);
            if (EditorGUI.EndChangeCheck())
            {
                halfHeight = Mathf.Max(0f, halfHeight);
            }

            return new Vector4(halfWidth * 2f, halfHeight * 2f, maxRange, minRange);
        }

        //copy of CoreLightEditorUtilities
        static Vector3[] GetFrustrumProjectedRectAngles(float distance, float aspect, float tanFOV)
        {
            Vector3 sizeX;
            Vector3 sizeY;
            float minXYTruncSize = distance * tanFOV;
            if (aspect >= 1.0f)
            {
                sizeX = new Vector3(minXYTruncSize * aspect, 0, 0);
                sizeY = new Vector3(0, minXYTruncSize, 0);
            }
            else
            {
                sizeX = new Vector3(minXYTruncSize, 0, 0);
                sizeY = new Vector3(0, minXYTruncSize / aspect, 0);
            }

            Vector3 center = new Vector3(0, 0, distance);
            Vector3[] angles =
            {
            center + sizeX + sizeY,
            center - sizeX + sizeY,
            center - sizeX - sizeY,
            center + sizeX - sizeY
        };

            return angles;
        }

        static Vector3[] GetSphericalProjectedRectAngles(float distance, float aspect, float tanFOV)
        {
            var angles = GetFrustrumProjectedRectAngles(distance, aspect, tanFOV);
            for (int index = 0; index < 4; ++index)
                angles[index] = angles[index].normalized * distance;
            return angles;
        }

        //TODO: decompose arguments (or tuples) + put back to CoreLightEditorUtilities
        // Same as Gizmo.DrawFrustum except that when aspect is below one, fov represent fovX instead of fovY
        // Use to match our light frustum pyramid behavior
        static void DrawSpherePortionWireframe(Vector4 aspectFovMaxRangeMinRange, float distanceTruncPlane = 0f)
        {
            float aspect = aspectFovMaxRangeMinRange.x;
            float fov = aspectFovMaxRangeMinRange.y;
            float maxRange = aspectFovMaxRangeMinRange.z;
            float minRange = aspectFovMaxRangeMinRange.w;
            float tanfov = Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f);

            var startAngles = new Vector3[4];
            if (minRange > 0f)
            {
                startAngles = GetFrustrumProjectedRectAngles(minRange, aspect, tanfov);
                Handles.DrawLine(startAngles[0], startAngles[1]);
                Handles.DrawLine(startAngles[1], startAngles[2]);
                Handles.DrawLine(startAngles[2], startAngles[3]);
                Handles.DrawLine(startAngles[3], startAngles[0]);
            }

            if (distanceTruncPlane > 0f)
            {
                var truncAngles = GetFrustrumProjectedRectAngles(distanceTruncPlane, aspect, tanfov);
                Handles.DrawLine(truncAngles[0], truncAngles[1]);
                Handles.DrawLine(truncAngles[1], truncAngles[2]);
                Handles.DrawLine(truncAngles[2], truncAngles[3]);
                Handles.DrawLine(truncAngles[3], truncAngles[0]);
            }

            var endAngles = GetSphericalProjectedRectAngles(maxRange, aspect, tanfov);
            var planProjectedCrossNormal0 = new Vector3(endAngles[0].y, -endAngles[0].x, 0).normalized;
            var planProjectedCrossNormal1 = new Vector3(endAngles[1].y, -endAngles[1].x, 0).normalized;
            Vector3[] faceNormals = new[]
            {
            Vector3.right - Vector3.Dot((endAngles[3] + endAngles[0]).normalized, Vector3.right) * (endAngles[3] + endAngles[0]).normalized,
            Vector3.up - Vector3.Dot((endAngles[0] + endAngles[1]).normalized, Vector3.up) * (endAngles[0] + endAngles[1]).normalized,
            Vector3.left - Vector3.Dot((endAngles[1] + endAngles[2]).normalized, Vector3.left) * (endAngles[1] + endAngles[2]).normalized,
            Vector3.down - Vector3.Dot((endAngles[2] + endAngles[3]).normalized, Vector3.down) * (endAngles[2] + endAngles[3]).normalized,
            //cross
            planProjectedCrossNormal0 - Vector3.Dot((endAngles[1] + endAngles[3]).normalized, planProjectedCrossNormal0) * (endAngles[1] + endAngles[3]).normalized,
            planProjectedCrossNormal1 - Vector3.Dot((endAngles[0] + endAngles[2]).normalized, planProjectedCrossNormal1) * (endAngles[0] + endAngles[2]).normalized,
        };

            float[] faceAngles = new[]
            {
            Vector3.Angle(endAngles[3], endAngles[0]),
            Vector3.Angle(endAngles[0], endAngles[1]),
            Vector3.Angle(endAngles[1], endAngles[2]),
            Vector3.Angle(endAngles[2], endAngles[3]),
            Vector3.Angle(endAngles[1], endAngles[3]),
            Vector3.Angle(endAngles[0], endAngles[2]),
        };

            Handles.DrawWireArc(Vector3.zero, faceNormals[0], endAngles[0], faceAngles[0], maxRange);
            Handles.DrawWireArc(Vector3.zero, faceNormals[1], endAngles[1], faceAngles[1], maxRange);
            Handles.DrawWireArc(Vector3.zero, faceNormals[2], endAngles[2], faceAngles[2], maxRange);
            Handles.DrawWireArc(Vector3.zero, faceNormals[3], endAngles[3], faceAngles[3], maxRange);
            Handles.DrawWireArc(Vector3.zero, faceNormals[4], endAngles[0], faceAngles[4], maxRange);
            Handles.DrawWireArc(Vector3.zero, faceNormals[5], endAngles[1], faceAngles[5], maxRange);

            Handles.DrawLine(startAngles[0], endAngles[0]);
            Handles.DrawLine(startAngles[1], endAngles[1]);
            Handles.DrawLine(startAngles[2], endAngles[2]);
            Handles.DrawLine(startAngles[3], endAngles[3]);
        }


        //copy of CoreLightEditorUtilities
        static Vector2 SliderPlaneHandle(Vector3 origin, Vector3 axis1, Vector3 axis2, Vector2 position)
        {
            Vector3 pos = origin + position.x * axis1 + position.y * axis2;
            float sizeHandle = HandleUtility.GetHandleSize(pos);
            bool temp = GUI.changed;
            GUI.changed = false;
            pos = Handles.Slider2D(pos, Vector3.forward, axis1, axis2, sizeHandle * 0.03f, Handles.DotHandleCap, 0f);
            if (GUI.changed)
            {
                position = new Vector2(Vector3.Dot(pos, axis1), Vector3.Dot(pos, axis2));
            }

            GUI.changed |= temp;
            return position;
        }


        //TODO: decompose arguments (or tuples) + put back to CoreLightEditorUtilities
        static Vector4 DrawSpherePortionHandle(Vector4 aspectFovMaxRangeMinRange, bool useNearPlane, float minAspect = 0.05f, float maxAspect = 20f, float minFov = 1f)
        {
            float aspect = aspectFovMaxRangeMinRange.x;
            float fov = aspectFovMaxRangeMinRange.y;
            float maxRange = aspectFovMaxRangeMinRange.z;
            float minRange = aspectFovMaxRangeMinRange.w;
            float tanfov = Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f);

            var endAngles = GetSphericalProjectedRectAngles(maxRange, aspect, tanfov);

            if (useNearPlane)
            {
                minRange = SliderLineHandle(Vector3.zero, Vector3.forward, minRange);
            }

            maxRange = SliderLineHandle(Vector3.zero, Vector3.forward, maxRange);

            float distanceRight = HandleUtility.DistanceToLine(endAngles[0], endAngles[3]);
            float distanceLeft = HandleUtility.DistanceToLine(endAngles[1], endAngles[2]);
            float distanceUp = HandleUtility.DistanceToLine(endAngles[0], endAngles[1]);
            float distanceDown = HandleUtility.DistanceToLine(endAngles[2], endAngles[3]);

            int pointIndex = 0;
            if (distanceRight < distanceLeft)
            {
                if (distanceUp < distanceDown)
                    pointIndex = 0;
                else
                    pointIndex = 3;
            }
            else
            {
                if (distanceUp < distanceDown)
                    pointIndex = 1;
                else
                    pointIndex = 2;
            }

            Vector2 send = endAngles[pointIndex];
            Vector3 farEnd = new Vector3(0, 0, endAngles[0].z);
            EditorGUI.BeginChangeCheck();
            Vector2 received = SliderPlaneHandle(farEnd, Vector3.right, Vector3.up, send);
            if (EditorGUI.EndChangeCheck())
            {
                bool fixedFov = Event.current.control && !Event.current.shift;
                bool fixedAspect = Event.current.shift && !Event.current.control;

                //work on positive quadrant
                int xSign = send.x < 0f ? -1 : 1;
                int ySign = send.y < 0f ? -1 : 1;
                Vector2 corrected = new Vector2(received.x * xSign, received.y * ySign);

                //fixed aspect correction
                if (fixedAspect)
                {
                    corrected.x = corrected.y * aspect;
                }

                //remove aspect deadzone
                if (corrected.x > maxAspect * corrected.y)
                {
                    corrected.y = corrected.x * minAspect;
                }

                if (corrected.x < minAspect * corrected.y)
                {
                    corrected.x = corrected.y / maxAspect;
                }

                //remove fov deadzone
                float deadThresholdFoV = Mathf.Tan(Mathf.Deg2Rad * minFov * 0.5f) * maxRange;
                corrected.x = Mathf.Max(corrected.x, deadThresholdFoV);
                corrected.y = Mathf.Max(corrected.y, deadThresholdFoV, Mathf.Epsilon * 100); //prevent any division by zero

                if (!fixedAspect)
                {
                    aspect = corrected.x / corrected.y;
                }

                float min = Mathf.Min(corrected.x, corrected.y);
                if (!fixedFov && maxRange > Mathf.Epsilon * 100)
                {
                    fov = Mathf.Atan(min / maxRange) * 2f * Mathf.Rad2Deg;
                }
            }

            return new Vector4(aspect, fov, maxRange, minRange);
        }

        protected override void OnSceneGUI()
        {
            var light = serializedObject.targetObject as Light;
            var type = light.type;
            var shape = light.shape;

            //if (!light.TryGetComponent<AdditionalLightData>(out var additionalLightData))
            //    additionalLightData = light.gameObject.AddComponent<AdditionalLightData>();

            switch (type)
            {
                case LightType.Directional:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                        DrawDirectionalLightGizmo(light);
                    break;
                case LightType.Point:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, Quaternion.identity, Vector3.one)))
                        DrawPointLightGizmo(light);
                    break;
                case LightType.Spot:
                    if (additionalLightData.AreaLightType != AreaLightType.None)
                    {
                        using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                        {
                            DrawPointLightGizmo(light);
                            CoreLightEditorUtilities.DrawRectangleLightGizmo(light);
                            additionalLightData.ShapeWidth = light.areaSize.x;
                            additionalLightData.ShapeHeight = light.areaSize.y;
                        }
                    }
                    else
                    {
                        using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                            switch (shape)
                            {
                                case LightShape.Cone:
                                    CoreLightEditorUtilities.DrawSpotLightGizmo(light);
                                    break;
                                case LightShape.Pyramid:
                                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                                    {
                                        Vector4 aspectFovMaxRangeMinRange = new Vector4(additionalLightData.ShapeWidth / additionalLightData.ShapeHeight, light.spotAngle, light.range);
                                        Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                                        Handles.color = light.color;
                                        DrawSpherePortionWireframe(aspectFovMaxRangeMinRange, light.shadowNearPlane);
                                        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                                        Handles.color = light.color;
                                        DrawSpherePortionWireframe(aspectFovMaxRangeMinRange, light.shadowNearPlane);
                                        EditorGUI.BeginChangeCheck();
                                        Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                                        Handles.color = light.color;
                                        aspectFovMaxRangeMinRange = DrawSpherePortionHandle(aspectFovMaxRangeMinRange, false);
                                        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                                        Handles.color = light.color;
                                        aspectFovMaxRangeMinRange = DrawSpherePortionHandle(aspectFovMaxRangeMinRange, false);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            Undo.RecordObject(light, "Adjust Pyramid Spot Light");
                                            light.spotAngle = aspectFovMaxRangeMinRange.y;
                                            light.range = aspectFovMaxRangeMinRange.z;

                                            var angle = light.spotAngle * Mathf.Deg2Rad * 0.5f;
                                            additionalLightData.ShapeWidth = light.range * Mathf.Sin(angle) / Mathf.Cos(angle) * aspectFovMaxRangeMinRange.x;
                                            additionalLightData.ShapeHeight = light.range * Mathf.Sin(angle) / Mathf.Cos(angle);
                                        }

                                        // Handles.color reseted at end of scope
                                    }

                                    break;
                                case LightShape.Box:
                                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                                    {
                                        Vector4 widthHeightMaxRangeMinRange = new Vector4(additionalLightData.ShapeWidth, additionalLightData.ShapeHeight, light.range);
                                        Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                                        Handles.color = light.color;
                                        DrawOrthoFrustumWireframe(widthHeightMaxRangeMinRange, light.shadowNearPlane);
                                        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                                        Handles.color = light.color;
                                        DrawOrthoFrustumWireframe(widthHeightMaxRangeMinRange, light.shadowNearPlane);
                                        EditorGUI.BeginChangeCheck();
                                        Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                                        Handles.color = light.color;
                                        widthHeightMaxRangeMinRange = DrawOrthoFrustumHandle(widthHeightMaxRangeMinRange, false);
                                        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                                        Handles.color = light.color;
                                        widthHeightMaxRangeMinRange = DrawOrthoFrustumHandle(widthHeightMaxRangeMinRange, false);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            Undo.RecordObject(light, "Adjust Box Spot Light");
                                            additionalLightData.ShapeWidth = widthHeightMaxRangeMinRange.x;
                                            additionalLightData.ShapeHeight = widthHeightMaxRangeMinRange.y;
                                            light.range = widthHeightMaxRangeMinRange.z;
                                        }

                                        // Handles.color reseted at end of scope
                                    }

                                    break;
                            }
                    }

                    case LightType.

                    break;
            }
        }

        [Flags]
        enum HandleDirections
        {
            Left = 1 << 0,
            Up = 1 << 1,
            Right = 1 << 2,
            Down = 1 << 3,
            All = Left | Up | Right | Down
        }

        static readonly Vector3[] directionalLightHandlesRayPositions =
        {
            new Vector3(1, 0, 0),
            new Vector3(-1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            new Vector3(1, 1, 0).normalized,
            new Vector3(1, -1, 0).normalized,
            new Vector3(-1, 1, 0).normalized,
            new Vector3(-1, -1, 0).normalized
        };

        /// <summary>
        /// Draw a gizmo for a directional light.
        /// </summary>
        /// <param name="light">The light that is used for this gizmo.</param>
        public static void DrawDirectionalLightGizmo(Light light)
        {
            // Sets the color of the Gizmo.
            Color outerColor = GetLightAboveObjectWireframeColor(light.color);

            Vector3 lightPos = light.transform.position;
            float lightSize;
            using (new Handles.DrawingScope(Matrix4x4.identity))    //be sure no matrix affect the size computation
            {
                lightSize = HandleUtility.GetHandleSize(lightPos);
            }
            float radius = lightSize * 0.2f;

            using (new Handles.DrawingScope(outerColor))
            {
                Handles.DrawWireDisc(Vector3.zero, Vector3.forward, radius);
                foreach (Vector3 normalizedPos in directionalLightHandlesRayPositions)
                {
                    Vector3 pos = normalizedPos * radius;
                    Handles.DrawLine(pos, pos + new Vector3(0, 0, lightSize));
                }
            }
        }

        /// <summary>
        /// Draw a gizmo for a point light.
        /// </summary>
        /// <param name="light">The light that is used for this gizmo.</param>
        public static void DrawPointLightGizmo(Light light)
        {
            // Sets the color of the Gizmo.
            Color outerColor = GetLightAboveObjectWireframeColor(light.color);

            // Drawing the point light
            DrawPointLight(light, outerColor);

            // Draw the handles and labels
            DrawPointHandlesAndLabels(light);
        }

        static void DrawPointLight(Light light, Color outerColor)
        {
            float range = light.range;

            using (new Handles.DrawingScope(outerColor))
            {
                EditorGUI.BeginChangeCheck();
                //range = Handles.RadiusHandle(Quaternion.identity, light.transform.position, range, false, true);
                range = DoPointHandles(range);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(light, "Adjust Point Light");
                    light.range = range;
                }
            }
        }

        static void DrawPointHandlesAndLabels(Light light)
        {
            // Getting the first control on point handle
            var firstControl = GUIUtility.GetControlID(s_PointLightHandle.GetHashCode(), FocusType.Passive) - 6; // BoxBoundsHandle allocates 6 control IDs
            if (Event.current.type != EventType.Repaint)
                return;
            //            var firstControl = GUIUtility.GetControlID(k_RadiusHandleHash, FocusType.Passive) - 6;
            //            if (Event.current.type != EventType.Repaint)
            //                return;

            // Adding label /////////////////////////////////////
            Vector3 labelPosition = Vector3.zero;

            if (GUIUtility.hotControl != 0)
            {
                switch (GUIUtility.hotControl - firstControl)
                {
                    case 0:
                        labelPosition = Vector3.right * light.range;
                        break;
                    case 1:
                        labelPosition = Vector3.left * light.range;
                        break;
                    case 2:
                        labelPosition = Vector3.up * light.range;
                        break;
                    case 3:
                        labelPosition = Vector3.down * light.range;
                        break;
                    case 4:
                        labelPosition = Vector3.forward * light.range;
                        break;
                    case 5:
                        labelPosition = Vector3.back * light.range;
                        break;
                    default:
                        return;
                }

                string labelText = FormattableString.Invariant($"Range: {light.range:0.00}");
                DrawHandleLabel(labelPosition, labelText);
            }
        }

        /// <summary>
        /// Draw a gizmo for an area/rectangle light.
        /// </summary>
        /// <param name="light">The light that is used for this gizmo.</param>
        public static void DrawRectangleLightGizmo(Light light)
        {
            // Color to use for gizmo drawing
            Color outerColor = GetLightAboveObjectWireframeColor(light.color);

            // Drawing the gizmo
            DrawRectangleLight(light, outerColor);

            // Draw the handles and labels
            DrawRectangleHandlesAndLabels(light);
        }

        static void DrawRectangleLight(Light light, Color outerColor)
        {
            Vector2 size = light.areaSize;
            var range = light.range;
            var innerColor = GetLightBehindObjectWireframeColor(light.color);
            DrawZTestedLine(range, outerColor, innerColor);

            using (new Handles.DrawingScope(outerColor))
            {
                EditorGUI.BeginChangeCheck();
                size = DoRectHandles(size);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(light, "Adjust Area Rectangle Light");
                    light.areaSize = size;
                }
            }
        }

        static void DrawRectangleHandlesAndLabels(Light light)
        {
            // Getting the first control on radius handle
            var firstControl = GUIUtility.GetControlID(s_AreaLightHandle.GetHashCode(), FocusType.Passive) - 6; // BoxBoundsHandle allocates 6 control IDs
            if (Event.current.type != EventType.Repaint)
                return;

            // Adding label /////////////////////////////////////
            Vector3 labelPosition = Vector3.zero;

            if (GUIUtility.hotControl != 0)
            {
                switch (GUIUtility.hotControl - firstControl)
                {
                    case 0: // PositiveX
                        labelPosition = Vector3.right * (light.areaSize.x / 2);
                        break;
                    case 1: // NegativeX
                        labelPosition = Vector3.left * (light.areaSize.x / 2);
                        break;
                    case 2: // PositiveY
                        labelPosition = Vector3.up * (light.areaSize.y / 2);
                        break;
                    case 3: // NegativeY
                        labelPosition = Vector3.down * (light.areaSize.y / 2);
                        break;
                    default:
                        return;
                }
                string labelText = FormattableString.Invariant($"w:{light.areaSize.x:0.00} x h:{light.areaSize.y:0.00}");
                DrawHandleLabel(labelPosition, labelText);
            }
        }

        /// <summary>
        /// Draw a gizmo for a disc light.
        /// </summary>
        /// <param name="light">The light that is used for this gizmo.</param>
        public static void DrawDiscLightGizmo(Light light)
        {
            // Color to use for gizmo drawing.
            Color outerColor = GetLightAboveObjectWireframeColor(light.color);

            // Drawing before objects
            DrawDiscLight(light, outerColor);

            // Draw handles
            DrawDiscHandlesAndLabels(light);
        }

        static void DrawDiscLight(Light light, Color outerColor)
        {
            float radius = light.areaSize.x;
            var range = light.range;
            var innerColor = GetLightBehindObjectWireframeColor(light.color);
            DrawZTestedLine(range, outerColor, innerColor);

            using (new Handles.DrawingScope(outerColor))
            {
                EditorGUI.BeginChangeCheck();
                radius = DoDiscHandles(radius);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(light, "Adjust Area Disc Light");
                    light.areaSize = new Vector2(radius, light.areaSize.y);
                }
            }
        }

        static void DrawDiscHandlesAndLabels(Light light)
        {
            // Getting the first control on radius handle
            var firstControl = GUIUtility.GetControlID(s_DiscLightHandle.GetHashCode(), FocusType.Passive) - 6; // BoxBoundsHandle allocates 6 control IDs
            if (Event.current.type != EventType.Repaint)
                return;

            Vector3 labelPosition = Vector3.zero;

            if (GUIUtility.hotControl != 0)
            {
                switch (GUIUtility.hotControl - firstControl)
                {
                    case 0: // PositiveX
                        labelPosition = Vector3.right * light.areaSize.x;
                        break;
                    case 1: // NegativeX
                        labelPosition = Vector3.left * light.areaSize.x;
                        break;
                    case 2: // PositiveY
                        labelPosition = Vector3.up * light.areaSize.x;
                        break;
                    case 3: // NegativeY
                        labelPosition = Vector3.down * light.areaSize.x;
                        break;
                    default:
                        return;
                }
                string labelText = FormattableString.Invariant($"Radius: {light.areaSize.x:0.00}");
                DrawHandleLabel(labelPosition, labelText);
            }
        }

        static void DrawWithZTest(PrimitiveBoundsHandle primitiveHandle, float alpha = 0.2f)
        {
            primitiveHandle.center = Vector3.zero;

            primitiveHandle.handleColor = Color.clear;
            primitiveHandle.wireframeColor = Color.white;
            Handles.zTest = CompareFunction.LessEqual;
            primitiveHandle.DrawHandle();

            primitiveHandle.wireframeColor = new Color(1f, 1f, 1f, alpha);
            Handles.zTest = CompareFunction.Greater;
            primitiveHandle.DrawHandle();

            primitiveHandle.handleColor = Color.white;
            primitiveHandle.wireframeColor = Color.clear;
            Handles.zTest = CompareFunction.Always;
            primitiveHandle.DrawHandle();
        }

        static void DrawZTestedLine(float range, Color outerColor, Color innerColor)
        {
            using (new Handles.DrawingScope(outerColor))
            {
                Handles.zTest = CompareFunction.LessEqual;
                Handles.DrawLine(Vector3.zero, Vector3.forward * range);
            }
            using (new Handles.DrawingScope(innerColor))
            {
                Handles.zTest = CompareFunction.Greater;
                Handles.DrawLine(Vector3.zero, Vector3.forward * range);
            }
            Handles.zTest = CompareFunction.Always;
        }

        static void DrawHandleLabel(Vector3 handlePosition, string labelText, float offsetFromHandle = 0.3f)
        {
            Vector3 labelPosition = Vector3.zero;

            var style = new GUIStyle { normal = { background = Texture2D.whiteTexture } };
            GUI.color = new Color(0.82f, 0.82f, 0.82f, 1);

            labelPosition = handlePosition + Handles.inverseMatrix.MultiplyVector(Vector3.up) * HandleUtility.GetHandleSize(handlePosition) * offsetFromHandle;
            Handles.Label(labelPosition, labelText, style);
        }

        static readonly BoxBoundsHandle s_AreaLightHandle =
            new BoxBoundsHandle { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y };

        static Vector2 DoRectHandles(Vector2 size)
        {
            s_AreaLightHandle.center = Vector3.zero;
            s_AreaLightHandle.size = size;
            DrawWithZTest(s_AreaLightHandle);

            return s_AreaLightHandle.size;
        }

        static readonly SphereBoundsHandle s_DiscLightHandle =
            new SphereBoundsHandle { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y };
        static float DoDiscHandles(float radius)
        {
            s_DiscLightHandle.center = Vector3.zero;
            s_DiscLightHandle.radius = radius;
            DrawWithZTest(s_DiscLightHandle);

            return s_DiscLightHandle.radius;
        }

        static readonly SphereBoundsHandle s_PointLightHandle =
            new SphereBoundsHandle { axes = PrimitiveBoundsHandle.Axes.All };

        static float DoPointHandles(float range)
        {
            s_PointLightHandle.radius = range;
            DrawWithZTest(s_PointLightHandle);

            return s_PointLightHandle.radius;
        }

        static bool drawInnerConeAngle = true;

        /// <summary>
        /// Draw a gizmo for a spot light.
        /// </summary>
        /// <param name="light">The light that is used for this gizmo.</param>
        public static void DrawSpotLightGizmo(Light light)
        {
            // Saving the default colors
            var defColor = Handles.color;
            var defZTest = Handles.zTest;

            // Default Color for outer cone will be Yellow if nothing has been provided.
            Color outerColor = GetLightAboveObjectWireframeColor(light.color);

            // The default z-test outer color will be 20% opacity of the outer color
            Color outerColorZTest = GetLightBehindObjectWireframeColor(outerColor);

            // Default Color for inner cone will be Yellow-ish if nothing has been provided.
            Color innerColor = GetLightInnerConeColor(light.color);

            // The default z-test outer color will be 20% opacity of the inner color
            Color innerColorZTest = GetLightBehindObjectWireframeColor(innerColor);

            // Drawing before objects
            Handles.zTest = CompareFunction.LessEqual;
            DrawSpotlightWireframe(light, outerColor, innerColor);

            // Drawing behind objects
            Handles.zTest = CompareFunction.Greater;
            DrawSpotlightWireframe(light, outerColorZTest, innerColorZTest);

            // Resets the compare function to always
            Handles.zTest = CompareFunction.Always;

            // Draw handles
            if (!Event.current.alt)
            {
                DrawHandlesAndLabels(light, outerColor);
            }

            // Resets the handle colors
            Handles.color = defColor;
            Handles.zTest = defZTest;
        }

        static void DrawHandlesAndLabels(Light light, Color color)
        {
            // Zero position vector3
            Vector3 zeroPos = Vector3.zero;

            // Variable for which direction to draw the handles
            HandleDirections DrawHandleDirections;

            // Draw the handles ///////////////////////////////
            Handles.color = color;

            // Draw Center Handle
            float range = light.range;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUI.BeginChangeCheck();
            range = SliderLineHandle(id, Vector3.zero, Vector3.forward, range, "Range: ");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(new[] { light }, "Undo range change.");
            }

            // Draw outer handles
            DrawHandleDirections = HandleDirections.Down | HandleDirections.Up;
            const string outerLabel = "Outer Angle: ";

            EditorGUI.BeginChangeCheck();
            float outerAngle = DrawConeHandles(zeroPos, light.spotAngle, range, DrawHandleDirections, outerLabel);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(new[] { light }, "Undo outer angle change.");
            }

            // Draw inner handles
            float innerAngle = 0;
            const string innerLabel = "Inner Angle: ";
            if (light.innerSpotAngle > 0f && drawInnerConeAngle)
            {
                DrawHandleDirections = HandleDirections.Left | HandleDirections.Right;
                EditorGUI.BeginChangeCheck();
                innerAngle = DrawConeHandles(zeroPos, light.innerSpotAngle, range, DrawHandleDirections, innerLabel);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObjects(new[] { light }, "Undo inner angle change.");
                }
            }

            // Draw Near Plane Handle
            float nearPlaneRange = light.shadowNearPlane;
            if (light.shadows != LightShadows.None && light.lightmapBakeType != LightmapBakeType.Baked)
            {
                EditorGUI.BeginChangeCheck();
                nearPlaneRange = SliderLineHandle(GUIUtility.GetControlID(FocusType.Passive), Vector3.zero, Vector3.forward, nearPlaneRange, "Near Plane: ");
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObjects(new[] { light }, "Undo shadow near plane change.");
                    nearPlaneRange = Mathf.Clamp(nearPlaneRange, 0.1f, light.range);
                }
            }

            // If changes has been made we update the corresponding property
            if (GUI.changed)
            {
                light.spotAngle = outerAngle;
                light.innerSpotAngle = innerAngle;
                light.range = Math.Max(range, 0.01f);
                light.shadowNearPlane = Mathf.Clamp(nearPlaneRange, 0.1f, light.range);
            }
        }

        static Color GetLightInnerConeColor(Color wireframeColor)
        {
            Color color = wireframeColor;
            color.a = 0.4f;
            return RemapLightColor(color.linear);
        }

        static Color GetLightAboveObjectWireframeColor(Color wireframeColor)
        {
            Color color = wireframeColor;
            color.a = 1f;
            return RemapLightColor(color.linear);
        }

        static Color GetLightBehindObjectWireframeColor(Color wireframeColor)
        {
            Color color = wireframeColor;
            color.a = 0.2f;
            return RemapLightColor(color.linear);
        }

        static Color RemapLightColor(Color src)
        {
            Color color = src;
            float max = Mathf.Max(Mathf.Max(color.r, color.g), color.b);
            if (max > 0f)
            {
                float mult = 1f / max;
                color.r *= mult;
                color.g *= mult;
                color.b *= mult;
            }
            else
            {
                color = Color.white;
            }

            return color;
        }

        static void DrawSpotlightWireframe(Light spotlight, Color outerColor, Color innerColor)
        {
            // Variable for which direction to draw the handles
            HandleDirections DrawHandleDirections;

            float outerAngle = spotlight.spotAngle;
            float innerAngle = spotlight.innerSpotAngle;
            float range = spotlight.range;

            var outerDiscRadius = range * Mathf.Sin(outerAngle * Mathf.Deg2Rad * 0.5f);
            var outerDiscDistance = Mathf.Cos(Mathf.Deg2Rad * outerAngle * 0.5f) * range;
            var vectorLineUp = Vector3.Normalize(Vector3.forward * outerDiscDistance + Vector3.up * outerDiscRadius);
            var vectorLineLeft = Vector3.Normalize(Vector3.forward * outerDiscDistance + Vector3.left * outerDiscRadius);

            // Need to check if we need to draw inner angle
            // Need to disable this for now until we get all the inner angle baking working.
            if (innerAngle > 0f && drawInnerConeAngle)
            {
                DrawHandleDirections = HandleDirections.Up | HandleDirections.Down;
                var innerDiscRadius = range * Mathf.Sin(innerAngle * Mathf.Deg2Rad * 0.5f);
                var innerDiscDistance = Mathf.Cos(Mathf.Deg2Rad * innerAngle * 0.5f) * range;

                // Drawing the inner Cone and also z-testing it to draw another color if behind
                Handles.color = innerColor;
                DrawConeWireframe(innerDiscRadius, innerDiscDistance, DrawHandleDirections);
            }

            // Draw range line
            Handles.color = innerColor;
            var rangeCenter = Vector3.forward * range;
            Handles.DrawLine(Vector3.zero, rangeCenter);

            // Drawing the outer Cone and also z-testing it to draw another color if behind
            Handles.color = outerColor;

            DrawHandleDirections = HandleDirections.Left | HandleDirections.Right;
            DrawConeWireframe(outerDiscRadius, outerDiscDistance, DrawHandleDirections);

            // Bottom arcs, making a nice rounded shape
            Handles.DrawWireArc(Vector3.zero, Vector3.right, vectorLineUp, outerAngle, range);
            Handles.DrawWireArc(Vector3.zero, Vector3.up, vectorLineLeft, outerAngle, range);

            // If we are using shadows we draw the near plane for shadows
            if (spotlight.shadows != LightShadows.None && spotlight.lightmapBakeType != LightmapBakeType.Baked)
            {
                DrawShadowNearPlane(spotlight, innerColor);
            }
        }

        static void DrawShadowNearPlane(Light spotlight, Color color)
        {
            Color previousColor = Handles.color;
            Handles.color = color;

            var shadowDiscRadius = Mathf.Tan(spotlight.spotAngle * Mathf.Deg2Rad * 0.5f) * spotlight.shadowNearPlane;
            var shadowDiscDistance = spotlight.shadowNearPlane;
            Handles.DrawWireDisc(Vector3.forward * shadowDiscDistance, Vector3.forward, shadowDiscRadius);
            Handles.DrawLine(Vector3.forward * shadowDiscDistance, (Vector3.right * shadowDiscRadius) + (Vector3.forward * shadowDiscDistance));
            Handles.DrawLine(Vector3.forward * shadowDiscDistance, (-Vector3.right * shadowDiscRadius) + (Vector3.forward * shadowDiscDistance));

            Handles.color = previousColor;
        }

        static void DrawConeWireframe(float radius, float height, HandleDirections handleDirections)
        {
            var rangeCenter = Vector3.forward * height;
            if (handleDirections.HasFlag(HandleDirections.Up))
            {
                var rangeUp = rangeCenter + Vector3.up * radius;
                Handles.DrawLine(Vector3.zero, rangeUp);
            }

            if (handleDirections.HasFlag(HandleDirections.Down))
            {
                var rangeDown = rangeCenter - Vector3.up * radius;
                Handles.DrawLine(Vector3.zero, rangeDown);
            }

            if (handleDirections.HasFlag(HandleDirections.Right))
            {
                var rangeRight = rangeCenter + Vector3.right * radius;
                Handles.DrawLine(Vector3.zero, rangeRight);
            }

            if (handleDirections.HasFlag(HandleDirections.Left))
            {
                var rangeLeft = rangeCenter - Vector3.right * radius;
                Handles.DrawLine(Vector3.zero, rangeLeft);
            }

            //Draw Circle
            Handles.DrawWireDisc(rangeCenter, Vector3.forward, radius);
        }

        static float DrawConeHandles(Vector3 position, float angle, float range, HandleDirections handleDirections, string controlName)
        {
            if (handleDirections.HasFlag(HandleDirections.Left))
            {
                angle = SizeSliderSpotAngle(position, Vector3.forward, -Vector3.right, range, angle, controlName);
            }
            if (handleDirections.HasFlag(HandleDirections.Up))
            {
                angle = SizeSliderSpotAngle(position, Vector3.forward, Vector3.up, range, angle, controlName);
            }
            if (handleDirections.HasFlag(HandleDirections.Right))
            {
                angle = SizeSliderSpotAngle(position, Vector3.forward, Vector3.right, range, angle, controlName);
            }
            if (handleDirections.HasFlag(HandleDirections.Down))
            {
                angle = SizeSliderSpotAngle(position, Vector3.forward, -Vector3.up, range, angle, controlName);
            }
            return angle;
        }

        static float SliderLineHandle(int id, Vector3 position, Vector3 direction, float value, string labelText = "")
        {
            Vector3 pos = position + direction * value;
            float sizeHandle = HandleUtility.GetHandleSize(pos);
            bool temp = GUI.changed;
            GUI.changed = false;
            pos = Handles.Slider(id, pos, direction, sizeHandle * 0.03f, Handles.DotHandleCap, 0f);
            if (GUI.changed)
            {
                value = Vector3.Dot(pos - position, direction);
            }
            GUI.changed |= temp;

            if (GUIUtility.hotControl == id && !String.IsNullOrEmpty(labelText))
            {
                labelText += FormattableString.Invariant($"{value:0.00}");
                DrawHandleLabel(pos, labelText);
            }

            return value;
        }

        static float SizeSliderSpotAngle(Vector3 position, Vector3 forward, Vector3 axis, float range, float spotAngle, string controlName)
        {
            if (Math.Abs(spotAngle) <= 0.05f)
                return spotAngle;
            var angledForward = Quaternion.AngleAxis(Mathf.Max(spotAngle, 0.05f) * 0.5f, axis) * forward;
            var centerToLeftOnSphere = (angledForward * range + position) - (position + forward * range);
            bool temp = GUI.changed;
            GUI.changed = false;
            var handlePosition = position + forward * range;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            var newMagnitude = Mathf.Max(0f, SliderLineHandle(id, handlePosition, centerToLeftOnSphere.normalized, centerToLeftOnSphere.magnitude));
            if (GUI.changed)
            {
                centerToLeftOnSphere = centerToLeftOnSphere.normalized * newMagnitude;
                angledForward = (centerToLeftOnSphere + (position + forward * range) - position).normalized;
                spotAngle = Mathf.Clamp(Mathf.Acos(Vector3.Dot(forward, angledForward)) * Mathf.Rad2Deg * 2, 0f, 179f);
                if (spotAngle <= 0.05f || float.IsNaN(spotAngle))
                    spotAngle = 0f;
            }
            GUI.changed |= temp;

            if (GUIUtility.hotControl == id)
            {
                var pos = handlePosition + centerToLeftOnSphere.normalized * newMagnitude;
                string labelText = FormattableString.Invariant($"{controlName} {spotAngle:0.00}");
                DrawHandleLabel(pos, labelText);
            }

            return spotAngle;
        }
    }
}