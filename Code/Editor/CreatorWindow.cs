﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using Deform;

namespace DeformEditor
{
	public class CreatorWindow : EditorWindow
	{
		private static List<DeformerAttribute> deformerAttributes;
		private static List<DeformerAttribute> filteredDeformerAttributes;

		private class Styles
		{
			public const int MAX_LIST_BUTTON_WIDTH = 150;
			public const int PAD_X = 5;
			public const int PAD_Y = 2;

			public static GUIStyle ListButton;
			public static readonly GUIStyle ButtonLeftStyle, ButtonMidStyle, ButtonRightStyle;

			static Styles ()
			{
				ListButton = new GUIStyle (EditorStyles.miniButton);
				ListButton.margin = new RectOffset (PAD_X, PAD_X, PAD_Y, PAD_Y);

				ButtonLeftStyle = new GUIStyle (EditorStyles.miniButtonLeft);
				ButtonMidStyle = new GUIStyle (EditorStyles.miniButton);
				ButtonRightStyle = new GUIStyle (EditorStyles.miniButtonRight);

				ButtonLeftStyle.fixedHeight = ButtonMidStyle.fixedHeight = ButtonRightStyle.fixedHeight = EditorGUIUtility.singleLineHeight;

				var leftMargin = ButtonLeftStyle.margin;
				var midMargin = ButtonMidStyle.margin;
				var rightMargin = ButtonRightStyle.margin;

				// decrease the margins of the inner sides by 1 because in a horizontal layout Unity likes to randomly add 1px of padding between elements
				midMargin.left--;
				rightMargin.left--;
				midMargin.right--;
				leftMargin.right--;
			}
		}

		private static class Content
		{
			public static GUIContent CreateDeformable = new GUIContent (text: "Create Deformable", tooltip: "Create a deformable");
			public static GUIContent[] FilterToolbar;

			static Content ()
			{
				var noiseTexture = DeformEditorResources.LoadAssetOfType<Texture2D> ("DeformNoiseIcon");
				var maskTexture = DeformEditorResources.LoadAssetOfType<Texture2D> ("DeformMaskIcon");
				var utilityTexture = DeformEditorResources.LoadAssetOfType<Texture2D> ("DeformUtilityIcon");

				FilterToolbar = new GUIContent[]
				{
					new GUIContent ("All", "All"),
					new GUIContent ("N", "Normal"),
					new GUIContent (noiseTexture, "Noise"),
					new GUIContent (maskTexture, "Mask"),
					new GUIContent (utilityTexture, "Utility")
				};
			}
		}

		private enum FilterCategory { All, Normal, Noise, Mask, Utility }

		[SerializeField]
		private FilterCategory filter;
		[SerializeField]
		private Vector2 scrollPosition;
		[SerializeField]
		private SearchField searchField;
		[SerializeField]
		private string searchQuery;
		[SerializeField]
		private int searchIndex = -1;

		[MenuItem ("Window/Deform/Creator", priority = 10000)]
		[MenuItem ("Tools/Deform/Creator", priority = 10000)]
		public static void ShowWindow ()
		{
			GetWindow<CreatorWindow> ("Creator", true);
		}

		[UnityEditor.Callbacks.DidReloadScripts]
		private static void UpdateDeformerAttributes ()
		{
			deformerAttributes = GetAllDeformerAttributes ().OrderBy (x => (int)x.Category).ToList ();
		}

		private void OnEnable ()
		{
			searchField = new SearchField ();
			searchField.downOrUpArrowKeyPressed += SearchFieldIndexChange;

			UpdateDeformerAttributes ();

			Undo.undoRedoPerformed += Repaint;
		}

		private void OnDisable ()
		{
			Undo.undoRedoPerformed -= Repaint;
		}

		private void OnGUI ()
		{
			EditorGUILayout.Space ();

			if (GUILayout.Button (Content.CreateDeformable, Styles.ListButton))
				AddOrCreateDeformable ();

			EditorGUILayout.Space ();

			using (var check = new EditorGUI.ChangeCheckScope ())
			{
				var newCategoryIndex = GUILayout.Toolbar ((int)filter, Content.FilterToolbar, EditorStyles.miniButton, GUILayout.Height (EditorGUIUtility.singleLineHeight));
				if (check.changed)
				{
					Undo.RecordObject (this, "Changed Category Filter");
					filter = (FilterCategory)newCategoryIndex;
				}
			}

			using (new EditorGUILayout.HorizontalScope ())
			{
				using (var check = new EditorGUI.ChangeCheckScope ())
				{
					var rect = GUILayoutUtility.GetRect (1, 1, 18, 18, GUILayout.ExpandWidth (true));
					rect.width -= Styles.PAD_X * 2;
					rect.x += Styles.PAD_X;
					rect.y += Styles.PAD_Y * 2;

					var newSearchQuery = searchField.OnToolbarGUI (rect, searchQuery);
					if (check.changed)
					{
						Undo.RecordObject (this, "Changed Search Query");
						searchQuery = newSearchQuery;
					}
				}
			}

			using (var scroll = new EditorGUILayout.ScrollViewScope (scrollPosition))
			{
				if (deformerAttributes == null || deformerAttributes.Count == 0)
					EditorGUILayout.LabelField ("No deformers found.", GUILayout.MinWidth (0));
				else
				{
					filteredDeformerAttributes =
					(
						from d in deformerAttributes
						where AttributeIncludedInFilter (d, filter)
						where string.IsNullOrEmpty (searchQuery) || d.Name.ToLower ().Contains (searchQuery.ToLower ())
						select d
					).ToList ();

					var drawnCount = 0;
					for (int i = 0; i < filteredDeformerAttributes.Count; i++)
					{
						var current = filteredDeformerAttributes[i];

						if (AttributeIncludedInFilter (current, filter))
						{
							if (drawnCount == 0)
							{
								var countInCategory = filteredDeformerAttributes.Count (t => t.Category == current.Category);
								EditorGUILayout.LabelField ($"{current.Category.ToString ()} ({countInCategory})", EditorStyles.centeredGreyMiniLabel, GUILayout.MinWidth (0));
							}

							if (GUILayout.Button (new GUIContent (current.Name, current.Description), Styles.ListButton))
								CreateDeformerFromAttribute (current, Event.current.modifiers == EventModifiers.Alt);
							drawnCount++;
						}

						if (filter == FilterCategory.All)
						{
							if (i + 1 < filteredDeformerAttributes.Count)
							{
								var next = filteredDeformerAttributes[i + 1];
								if (next.Category != current.Category)
								{
									var countInCategory = filteredDeformerAttributes.Count (t => t.Category == next.Category);
									EditorGUILayout.LabelField ($"{next.Category.ToString ()} ({countInCategory})", EditorStyles.centeredGreyMiniLabel, GUILayout.MinWidth (0));
								}
							}
						}
					}

					EditorGUILayout.Space ();
				}
				scrollPosition = scroll.scrollPosition;
			}
		}

		private void SearchFieldIndexChange ()
		{
			var e = Event.current;

			if (e.keyCode == KeyCode.UpArrow)
			{
				searchIndex--;
				e.Use ();
			}
			else if (e.keyCode == KeyCode.DownArrow)
			{
				searchIndex++;
				e.Use ();
			}
			if (searchIndex < 0)
				searchIndex = filteredDeformerAttributes.Count - 1;
			if (searchIndex > filteredDeformerAttributes.Count - 1)
				searchIndex = 0;
		}

		private bool AttributeIncludedInFilter (DeformerAttribute attribute, FilterCategory filter)
		{
			if (filter == FilterCategory.All)
				return true;
			else if (filter == FilterCategory.Normal && attribute.Category == Category.Normal)
				return true;
			else if (filter == FilterCategory.Noise && attribute.Category == Category.Noise)
				return true;
			else if (filter == FilterCategory.Mask && attribute.Category == Category.Mask)
				return true;
			else if (filter == FilterCategory.Utility && attribute.Category == Category.Utility)
				return true;
			return false;
		}

		public void AddOrCreateDeformable ()
		{
			var targets = Selection.gameObjects;

			// If we don't have any objects selected, create a new Deformable.
			if (targets == null || targets.Length == 0)
				CreateDeformable ();
			else
			{
				// Keep track of whether or not we've actually been able to add a Deformable component.
				var addedComponent = false;
				foreach (var target in Selection.gameObjects)
				{
					// Check if there's already a Deformable/
					var deformable = target.GetComponent<Deformable> ();
					// If there isn't, we can add one
					if (!PrefabUtility.IsPartOfPrefabAsset (target) && deformable == null && MeshTarget.IsValid (target))
					{
						Undo.AddComponent<Deformable> (target);
						addedComponent = true;
					}
				}

				// If we never ended up adding a Deformable component, we should create new one.
				if (!addedComponent)
					CreateDeformable ();
			}
		}

		private Deformable CreateDeformable ()
		{
			var newObject = GameObject.CreatePrimitive (PrimitiveType.Sphere);
			newObject.name = "Deformable Object";

			var deformable = newObject.AddComponent<Deformable> ();
			deformable.ChangeMesh (DeformEditorResources.LoadAssetOfType<Mesh> ("DeformDefaultMesh"));

			DestroyImmediate (newObject.GetComponent<Collider> ());

			Selection.activeGameObject = newObject;

			Undo.RegisterCreatedObjectUndo
			(
				newObject,
				"Created Deformable GameObject"
			);

			return deformable;
		}

		public void CreateDeformerFromAttribute (DeformerAttribute attribute, bool autoAdd)
		{
			var selectedGameObjects = Selection.gameObjects;
			if (selectedGameObjects == null || selectedGameObjects.Length == 0)
			{
				var newGameObject = new GameObject (attribute.Name);

				Undo.RegisterCreatedObjectUndo (newGameObject, "Created Deformer");

				newGameObject.AddComponent (attribute.Type);

				newGameObject.transform.localRotation = Quaternion.Euler (attribute.XRotation, attribute.YRotation, attribute.ZRotation);

				Selection.activeGameObject = newGameObject;
			}
			else
			{
				Undo.SetCurrentGroupName ("Created Deformer");

				var newGameObject = new GameObject (attribute.Name);
				Undo.RegisterCreatedObjectUndo (newGameObject, "Created Deformer");

				UnityEditor.EditorGUIUtility.PingObject (newGameObject);

				var newDeformer = newGameObject.AddComponent (attribute.Type) as Deformer;

				if (autoAdd)
				{
					if (selectedGameObjects.Length == 1)
					{
						if (!PrefabUtility.IsPartOfPrefabAsset (Selection.gameObjects[0]))
						{
							var parent = selectedGameObjects[0].transform;
							newGameObject.transform.SetParent (parent, true);
							newGameObject.transform.position = parent.position;
							newGameObject.transform.rotation = parent.rotation * Quaternion.Euler (attribute.XRotation, attribute.YRotation, attribute.ZRotation);
						}
					}
					else
					{
						var center = GetAverageGameObjectPosition (selectedGameObjects);
						var rotation = Quaternion.Euler (attribute.XRotation, attribute.YRotation, attribute.ZRotation);
						newGameObject.transform.SetPositionAndRotation (center, rotation);
					}

					var deformables = GetComponents<Deformable> (selectedGameObjects);
					var groups = GetComponents<GroupDeformer> (selectedGameObjects);
					var repeaters = GetComponents<RepeaterDeformer> (selectedGameObjects);

					foreach (var deformable in deformables)
					{
						if (deformable != null && !PrefabUtility.IsPartOfPrefabAsset (deformable))
						{
							Undo.RecordObject (deformable, "Added Deformer");
							deformable.DeformerElements.Add (new DeformerElement (newDeformer));
						}
					}

					foreach (var group in groups)
					{
						if (group != null && !PrefabUtility.IsPartOfPrefabAsset (group))
						{
							Undo.RecordObject (group, "Added Deformer");
							group.DeformerElements.Add (new DeformerElement (newDeformer));
						}
					}

					foreach (var repeater in repeaters)
					{
						if (repeater != null && !PrefabUtility.IsPartOfPrefabAsset (repeater))
						{
							Undo.RecordObject (repeater, "Set Deformer");
							repeater.DeformerElement.Component = newDeformer;
						}
					}

				}
				else
					Selection.activeGameObject = newGameObject;

				Undo.CollapseUndoOperations (Undo.GetCurrentGroup ());
			}
		}

		private IEnumerable<T> GetComponents<T> (GameObject[] objects) where T : Component
		{
			for (int i = 0; i < objects.Length; i++)
			{
				var component = objects[i].GetComponent<T> ();
				if (component != null)
					yield return component;
			}
		}

		private Vector3 GetAverageGameObjectPosition (GameObject[] gameObjects)
		{
			if (gameObjects == null || gameObjects.Length == 0)
				return Vector3.zero;

			var sum = Vector3.zero;
			foreach (var gameObject in gameObjects)
				sum += gameObject.transform.position;

			return sum / gameObjects.Length;
		}

		public static IEnumerable<DeformerAttribute> GetAllDeformerAttributes ()
		{
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies ())
			{
				foreach (var type in assembly.GetTypes ())
				{
					if (type.IsSubclassOf (typeof (Deformer)))
					{
						var attribute = type.GetCustomAttribute<DeformerAttribute> (false);
						if (attribute != null)
							yield return attribute;
					}
				}
			}
		}
	}
}