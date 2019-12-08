using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TF
{
    [CustomPropertyDrawer(typeof(TFLayerAttribute))]
    public class TFLayerAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            TFLayerAttribute range = (TFLayerAttribute)attribute;
        }
    }
}