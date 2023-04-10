using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SanyaBane.EditorScript
{
  public class HierarchyMultiSelectionChildren : EditorWindow
  {
    private static HierarchyMultiSelectionChildren _window;

    private bool _showErrorNoSelectedParentsFound;

    private List<Transform> _rememberedTransformsList;
    private ReorderableList _rememberedTransformsReorderableList;
    private Vector2 _scrollBarPosition;

    private bool _expandChildrenAfterSelection;

    [MenuItem("Tools/Hierarchy Multi Selection Children2")]
    public static void Init()
    {
      _window = GetWindow<HierarchyMultiSelectionChildren>(true, nameof(HierarchyMultiSelectionChildren));
      _window.minSize = new Vector2(250, 400);
      _window.Show();
    }

    private void OnGUI()
    {
      InitClips();

      if (GUILayout.Button("Set currently selected GameObjects as \"Remembered parents\"", new GUILayoutOption[] { GUILayout.Height(30) }))
      {
        _showErrorNoSelectedParentsFound = false;

        if (Selection.gameObjects.Length == 0)
          return;

        _rememberedTransformsList = new List<Transform>(Selection.transforms);
      }

      if (GUILayout.Button("Append currently selected GameObjects into \"Remembered parents\"", new GUILayoutOption[] { GUILayout.Height(30) }))
      {
        _showErrorNoSelectedParentsFound = false;

        if (Selection.gameObjects.Length == 0)
          return;

        foreach (var transform in Selection.transforms)
        {
          if (_rememberedTransformsList.Contains(transform))
            continue;

          _rememberedTransformsList.Add(transform);
        }
      }

      _scrollBarPosition =
        EditorGUILayout.BeginScrollView(_scrollBarPosition, false, true, GUILayout.MaxHeight(150));

      using (new EditorGUI.DisabledScope())
      {
        _rememberedTransformsReorderableList.list = _rememberedTransformsList;
        _rememberedTransformsReorderableList.DoLayoutList();
      }

      EditorGUILayout.EndScrollView();

      GUILayout.Space(20);
      
      EditorGUIUtility.labelWidth = 200;
      string toggleText = "Expand children after selection";
      _expandChildrenAfterSelection = EditorGUILayout.Toggle(new GUIContent(toggleText, toggleText + '.'), _expandChildrenAfterSelection, GUILayout.ExpandWidth(true));

      GUILayout.Label("Now select a single child of any of previously selected GameObjects and press button bellow:", EditorStyles.wordWrappedLabel);
      using (new ChangeGuiEnabled(_rememberedTransformsList.Count != 0))
      {
        if (GUILayout.Button("Move selection of previously selected GameObjects to children", new GUILayoutOption[] { GUILayout.Height(40) }))
        {
          _showErrorNoSelectedParentsFound = false;

          if (Selection.transforms.Length != 1)
          {
            Debug.Log("You're supposed to select single child of previously selected GameObjects.");
            return;
          }

          var selectedTransform = Selection.transforms.First();

          IReadOnlyList<string> pathFromParentToChild = null;
          foreach (var rememberedTransform in _rememberedTransformsList)
          {
            pathFromParentToChild = GetPathFromChildToParentRecursively(rememberedTransform, selectedTransform);

            if (pathFromParentToChild != null)
              break;
          }

          if (pathFromParentToChild == null || pathFromParentToChild.Count == 0)
          {
            _showErrorNoSelectedParentsFound = true;
            return;
          }

          SelectChildOfPreviouslySelectedGameObjects(pathFromParentToChild);
        }

        GUILayout.Space(40);

        if (GUILayout.Button("Clear \"Remembered parents\"", new GUILayoutOption[] { GUILayout.Height(20) }))
        {
          _showErrorNoSelectedParentsFound = false;

          _rememberedTransformsList.Clear();
          return;
        }

        if (_showErrorNoSelectedParentsFound)
        {
          GUILayout.Space(10);

          using (new ChangeGuiColor(Color.red))
          {
            GUILayout.Label("Error! Not a one of previously selected GameObjects is parent of current selected GameObject:", EditorStyles.wordWrappedLabel);
          }
        }
      }
    }

    private void SelectChildOfPreviouslySelectedGameObjects(IReadOnlyList<string> pathFromParentToChild)
    {
      var childrenToSelect = new List<Transform>();

      for (int i = 0; i < _rememberedTransformsList.Count; i++)
      {
        var rememberedTransform = _rememberedTransformsList[i];

        var childToSelect = GetChildOfTransformByPath(rememberedTransform, pathFromParentToChild);
        if (childToSelect == null)
          continue;

        childrenToSelect.Add(childToSelect);
      }

      if (childrenToSelect.Count > 0)
      {
        var hierarchyExpandedGameObjectsBeforeApplySelection = HierarchySelection.GetHierarchyExpandedGameObjects().ToArray();

        var gameObjects = childrenToSelect.Select(x => x.gameObject).ToArray();

        // apply selection
        Selection.objects = gameObjects;
        
        _showErrorNoSelectedParentsFound = false;
        
        if (!_expandChildrenAfterSelection)
        {
          CollapseChildrenAfterApplyingSelection(pathFromParentToChild, gameObjects, hierarchyExpandedGameObjectsBeforeApplySelection);
        }
      }
    }

    private void CollapseChildrenAfterApplyingSelection(IReadOnlyList<string> pathFromParentToChild, GameObject[] gameObjects, GameObject[] hierarchyExpandedGameObjectsBeforeApplySelection)
    {
      // Hierarchy window does not immediately expands selected gameobjects. It does it after a bit of time.
      // That's why we subscribe to "delayCall" in order to wait untill automatical expanding is performed,
      // and then we collapse needed gameobjects (the ones, which were collapsed before our selection was applied).

      void EditorDelayCallCallback()
      {
        EditorApplication.delayCall -= EditorDelayCallCallback;

        CollapseTransformsWhichWereCollapsedBeforeSelection(pathFromParentToChild, gameObjects, hierarchyExpandedGameObjectsBeforeApplySelection);
      }

      EditorApplication.delayCall += EditorDelayCallCallback;
    }

    private void CollapseTransformsWhichWereCollapsedBeforeSelection(IReadOnlyList<string> pathFromParentToChild, GameObject[] gameObjects, GameObject[] hierarchyExpandedGameObjectsBeforeApplySelection)
    {
      // For every selected gameobject check if it was expanded before we applied selection, and if not, collapse it.
      // Go up recursively untill meet "Remembered parent".
      var pathFromChildToParent = Enumerable.Reverse(pathFromParentToChild).ToArray();
      foreach (var gameObject in gameObjects)
      {
        CollapseUp(gameObject.transform, pathFromChildToParent, hierarchyExpandedGameObjectsBeforeApplySelection);
      }
    }

    private void CollapseUp(Transform transform, IReadOnlyList<string> pathFromChildToParent, GameObject[] hierarchyExpandedGameObjectsBeforeApplySelection)
    {
      for (int i = 0; i < pathFromChildToParent.Count; i++)
      {
        if (transform.name != pathFromChildToParent[i])
          return;

        if (hierarchyExpandedGameObjectsBeforeApplySelection.Contains(transform.gameObject))
          return; // we can just "return", since if transform is expanded, it means that it's parent(s) are expanded too

        HierarchySelection.SetExpanded(transform.gameObject, false);

        var parent = transform.parent;
        if (parent == null)
          return;

        transform = parent.transform;
      }

      if (transform != null)
      {
        // we collapsed all children, now we need to collapse "remembered parent"

        if (!hierarchyExpandedGameObjectsBeforeApplySelection.Contains(transform.gameObject))
        {
          HierarchySelection.SetExpanded(transform.gameObject, false);
        }
      }
    }

    private Transform GetChildOfTransformByPath(Transform transform, IReadOnlyList<string> pathFromParentToChild)
    {
      for (int i = 0; i < pathFromParentToChild.Count; i++)
      {
        transform = GetChildOfTransformByName(transform, pathFromParentToChild[i]);

        if (transform == null)
          return null;
      }

      return transform;
    }

    private Transform GetChildOfTransformByName(Transform parent, string childName)
    {
      for (int i = 0; i < parent.childCount; i++)
      {
        var child = parent.GetChild(i);

        if (child.name == childName)
        {
          return child;
        }
      }

      return null;
    }

    private List<string> GetPathFromChildToParentRecursively(Transform lookingForParent, Transform child)
    {
      if (child == lookingForParent)
        return new List<string>();

      if (child.parent == null)
        return null;

      var ret = GetPathFromChildToParentRecursively(lookingForParent, child.parent);
      if (ret == null)
        return null;

      ret.Add(child.name);
      return ret;
    }

    private void InitClips()
    {
      if (_rememberedTransformsList == null)
        _rememberedTransformsList = new List<Transform>();

      if (_rememberedTransformsReorderableList == null)
      {
        _rememberedTransformsReorderableList = new ReorderableList(_rememberedTransformsList, typeof(Texture2D), false, true, false, true)
        {
          elementHeight = 20,
          drawHeaderCallback = (Rect rect) => { GUI.Label(rect, $"Remembered parents ({_rememberedTransformsList.Count}):", EditorStyles.label); },
          drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
          {
            var element = _rememberedTransformsList[index];

            // EditorGUI.PropertyField(new Rect(rect.x, rect.y, EditorGUIUtility.currentViewWidth, EditorGUIUtility.singleLineHeight), element.FindPropertyRelative("Type"), GUIContent.none);
            GUI.Label(rect, element.name);
          }
        };
      }
    }
    
    // https://forum.unity.com/threads/hierarchy-selection-select-first-child-of-selection-and-expand-collapse-selection.1059023/
    private class HierarchySelection
    {
      private static Type GetHierarchyWindowType()
      {
        return typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
      }

      private static EditorWindow GetHierarchyWindow()
      {
        EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
        return EditorWindow.focusedWindow;
      }
    
      public static object GetSceneHierarchyObject()
      {
        object sceneHierarchy = GetHierarchyWindowType().GetProperty("sceneHierarchy").GetValue(GetHierarchyWindow());
        return sceneHierarchy;
      }

      public static List<GameObject> GetHierarchyExpandedGameObjects()
      {
        object sceneHierarchy = HierarchySelection.GetSceneHierarchyObject();
        var methodInfo = sceneHierarchy.GetType().GetMethod("GetExpandedGameObjects", BindingFlags.Public | BindingFlags.Instance);
      
        var expandedGameObject = (List<GameObject>)methodInfo.Invoke(sceneHierarchy, new object[] { });
        return expandedGameObject;
      }
      
      public static void SetExpanded(GameObject obj, bool expand)
      {
        object sceneHierarchy = GetSceneHierarchyObject();
        var methodInfo = sceneHierarchy.GetType().GetMethod("ExpandTreeViewItem", BindingFlags.NonPublic | BindingFlags.Instance);

        methodInfo.Invoke(sceneHierarchy, new object[] { obj.GetInstanceID(), expand });
      }
    }
    
    private class ChangeGuiEnabled : IDisposable
    {
      private readonly bool _previousValue;

      public ChangeGuiEnabled(bool value)
      {
        _previousValue = GUI.enabled;
        GUI.enabled = value;
      }
    
      public void Dispose()
      {
        GUI.enabled = _previousValue;
      }
    }
    
    private class ChangeGuiColor : IDisposable
    {
      private readonly Color _previousColor;

      public ChangeGuiColor(Color color)
      {
        _previousColor = GUI.color;
        GUI.color = color;
      }
    
      public void Dispose()
      {
        GUI.color = _previousColor;
      }
    }
  }
}