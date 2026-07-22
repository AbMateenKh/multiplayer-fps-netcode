using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.FPS.Editor
{
    public static class LegacyFpsInputSetup
    {
        const string InputManagerPath = "ProjectSettings/InputManager.asset";

        enum InputType
        {
            KeyOrMouseButton = 0,
            MouseMovement = 1,
            JoystickAxis = 2,
        }

        readonly struct AxisDefinition
        {
            public readonly string Name;
            public readonly string Positive;
            public readonly string Negative;
            public readonly string AltPositive;
            public readonly float Gravity;
            public readonly float Dead;
            public readonly float Sensitivity;
            public readonly InputType Type;
            public readonly int Axis;
            public readonly bool Invert;

            public AxisDefinition(
                string name,
                string positive = "",
                string negative = "",
                string altPositive = "",
                float gravity = 1000f,
                float dead = 0.001f,
                float sensitivity = 1000f,
                InputType type = InputType.KeyOrMouseButton,
                int axis = 0,
                bool invert = false)
            {
                Name = name;
                Positive = positive;
                Negative = negative;
                AltPositive = altPositive;
                Gravity = gravity;
                Dead = dead;
                Sensitivity = sensitivity;
                Type = type;
                Axis = axis;
                Invert = invert;
            }
        }

        static readonly AxisDefinition[] RequiredAxes =
        {
            new("Look X", dead: 0.2f, sensitivity: 1f,
                gravity: 0f, type: InputType.JoystickAxis, axis: 3),
            new("Look Y", dead: 0.2f, sensitivity: 1f,
                gravity: 0f, type: InputType.JoystickAxis, axis: 4, invert: true),
            new("Aim", positive: "mouse 1", altPositive: "joystick button 4"),
            new("Fire", positive: "mouse 0", altPositive: "joystick button 5"),
            new("Sprint", positive: "left shift", altPositive: "joystick button 8"),
            new("Crouch", positive: "c", altPositive: "joystick button 9"),
            new("Gamepad Fire", positive: "joystick button 5"),
            new("Gamepad Aim", positive: "joystick button 4"),
            new("Gamepad Switch", dead: 0.2f, sensitivity: 1f,
                gravity: 0f, type: InputType.JoystickAxis, axis: 5),
            new("NextWeapon", positive: "e", negative: "q"),
            new("Pause Menu", positive: "escape", altPositive: "joystick button 7"),
            new("Reload", positive: "r", altPositive: "joystick button 2"),
        };

        [MenuItem("Tools/Portfolio/Validation/Repair Legacy FPS Input")]
        public static void EnsureRequiredAxes()
        {
            UnityEngine.Object inputManager =
                AssetDatabase.LoadAllAssetsAtPath(InputManagerPath).FirstOrDefault();
            if (inputManager == null)
                throw new InvalidOperationException("Could not load " + InputManagerPath);

            SerializedObject serialized = new(inputManager);
            SerializedProperty axes = serialized.FindProperty("m_Axes");
            if (axes == null || !axes.isArray)
                throw new InvalidOperationException("InputManager m_Axes array is unavailable.");

            HashSet<string> existing = new(StringComparer.Ordinal);
            for (int i = 0; i < axes.arraySize; i++)
            {
                existing.Add(
                    axes.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("m_Name")
                        .stringValue);
            }

            int added = 0;
            foreach (AxisDefinition definition in RequiredAxes)
            {
                if (existing.Contains(definition.Name))
                    continue;

                axes.InsertArrayElementAtIndex(axes.arraySize);
                SerializedProperty axis = axes.GetArrayElementAtIndex(axes.arraySize - 1);
                ResetAxis(axis);
                Set(axis, "m_Name", definition.Name);
                Set(axis, "positiveButton", definition.Positive);
                Set(axis, "negativeButton", definition.Negative);
                Set(axis, "altPositiveButton", definition.AltPositive);
                Set(axis, "gravity", definition.Gravity);
                Set(axis, "dead", definition.Dead);
                Set(axis, "sensitivity", definition.Sensitivity);
                Set(axis, "type", (int)definition.Type);
                Set(axis, "axis", definition.Axis);
                Set(axis, "invert", definition.Invert);
                added++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Input Setup] Added {added} missing Legacy FPS input entries.");
        }

        static void ResetAxis(SerializedProperty axis)
        {
            Set(axis, "m_Name", "");
            Set(axis, "descriptiveName", "");
            Set(axis, "descriptiveNegativeName", "");
            Set(axis, "negativeButton", "");
            Set(axis, "positiveButton", "");
            Set(axis, "altNegativeButton", "");
            Set(axis, "altPositiveButton", "");
            Set(axis, "gravity", 0f);
            Set(axis, "dead", 0f);
            Set(axis, "sensitivity", 0f);
            Set(axis, "snap", false);
            Set(axis, "invert", false);
            Set(axis, "type", 0);
            Set(axis, "axis", 0);
            Set(axis, "joyNum", 0);
        }

        static void Set(SerializedProperty parent, string name, string value) =>
            parent.FindPropertyRelative(name).stringValue = value;

        static void Set(SerializedProperty parent, string name, float value) =>
            parent.FindPropertyRelative(name).floatValue = value;

        static void Set(SerializedProperty parent, string name, int value) =>
            parent.FindPropertyRelative(name).intValue = value;

        static void Set(SerializedProperty parent, string name, bool value) =>
            parent.FindPropertyRelative(name).boolValue = value;
    }
}
