using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PixelCrushers.DialogueSystem;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class DSTextImporter : EditorWindow
{
	public DialogueDatabase targetDatabase;
	public List<string> filenames;
	private string _heldScript;
	private string _heldCondition;
	private string _heldSequence;
	private List<(int convo, int id)> _heldLinks = new();
	private List<string> _heldTitleLinks = new();
	private string _heldTitle;
	private Template _template;

	private ReorderableList list;
	private int _elementIndex;
	private List<string> elements;

	private List<(string title, DialogueEntry origin)> linkWhenDone = new();

	private SerializedProperty _targetDBProperty;
	private SerializedObject serializedObject;

	[MenuItem("Museum Game/Dialogue Importer")]
	private static void OpenWindow()
	{
		GetWindow<DSTextImporter>().Show();
	}

	[MenuItem("Assets/Create/Museum Game/New Dialogue File", false, 1)]
	private static void CreateNewAsset()
	{
		ProjectWindowUtil.CreateAssetWithContent(
			"New Conversation.dstext",
			"title: NewConversation\nnpc: System\n---\n\n===");
	}

	protected void OnEnable()
	{

		list = new ReorderableList(filenames, typeof(string), true, true, true, true);
		list.drawHeaderCallback += DrawListHeader;
		list.drawElementCallback += DrawListItem;
		list.onAddCallback += AddListItem;
		
	}

	void DrawListHeader(Rect rect)
	{
		EditorGUI.LabelField(rect, "Folders");
	}

	void DrawListItem(Rect rect, int index, bool isActive, bool isFocused)
	{
		if (index < 0 || index >= filenames.Count)
		{
			return;
		}

		filenames[index] = EditorGUI.TextField(new Rect(rect.x, rect.y + 2, rect.width, EditorGUIUtility.singleLineHeight), GUIContent.none, filenames[index]);
	}

	private void AddListItem(ReorderableList list)
	{
		string lastPath = "Assets";
		if (filenames.Count > 0)
		{
			lastPath = filenames.Last();
		}
		var fullPath = EditorUtility.OpenFolderPanel("Select Folder", lastPath, "");
		if (string.IsNullOrEmpty(fullPath)) return;

		// Truncate this if it's in the assets directory, it makes things way easier to read
		var displayPath = fullPath;
		if (fullPath.StartsWith(Application.dataPath))
		{
			displayPath = "Assets" + fullPath.Substring(Application.dataPath.Length);
		}
		else
		{
			displayPath = fullPath;
		}
		filenames.Add(displayPath);
	}

	private void OnGUI()
	{
		serializedObject = new(this);
		_targetDBProperty = serializedObject.FindProperty("targetDatabase");
		
		EditorGUILayout.HelpBox("WARNING!\nRunning this tool will overwrite same-named conversations in the Target Database", MessageType.Warning);

		GUILayout.Space(10);
		EditorGUILayout.PropertyField(_targetDBProperty);

		GUILayout.Space(10);
		list.DoLayoutList();

		GUILayout.Space(10);
		EditorGUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("Process Files", GUILayout.Width(120), GUILayout.Height(40)))
		{
			ProcessFiles();
		}
		GUILayout.Space(10);
		EditorGUILayout.EndHorizontal();

		serializedObject.ApplyModifiedProperties();
	}

	public void ProcessFiles()
	{
		foreach(string s in filenames)
		{
			if (Directory.Exists(s))
			{
				foreach(string file in Directory.GetFiles(s, "*.dstext", SearchOption.AllDirectories))
				{
					ProcessFile(file);
				}
				
				foreach (var linkData in linkWhenDone)
				{
					//Debug.Log($"Trying to rectify links {linkData.title}");
					Conversation convo = targetDatabase.GetConversation(linkData.origin.conversationID);
					DialogueEntry target = convo.GetDialogueEntry(linkData.title);
					if (target == null)
					{
						target = targetDatabase.conversations.Find(x => x.dialogueEntries.Exists(y => y.Title == linkData.title))?.GetDialogueEntry(linkData.title);
						if (target != null)
						{
							linkData.origin.outgoingLinks.Add(new(linkData.origin.conversationID, linkData.origin.id, target.conversationID, target.id));
						}
						else
						{
							Debug.LogError($"Couldn't find title to link to | {convo.Title} | {linkData.title}");
						}
					}
					else
					{
						linkData.origin.outgoingLinks.Add(new(linkData.origin.conversationID, linkData.origin.id, target.conversationID, target.id));
					}
				}
				linkWhenDone.Clear();
			}
			else
			{
				ProcessFile(s);
				//Debug.LogError($"Directory Doesn't Exist | {s}");
			}
		}
	}
	
	public void ProcessFile(string filename)
	{
		if(File.Exists(filename))
		{
			_template = Template.FromDefault();
			Conversation conversation = _template.CreateConversation(targetDatabase.conversations.Max(x => x.id) + 1, "PLACEHOLDERTITLE");
			DialogueEntry startNode = _template.CreateDialogueEntry(0, conversation.id, "START");
			startNode.Sequence = "None()"; // START node usually shouldn't play a sequence.
			startNode.ActorID = GetOrCreateActor("Player", true).id;
			conversation.dialogueEntries.Add(startNode);
			DialogueEntry current = startNode;
			DialogueEntry previous = new();
			
			List<Node> nodes = Parse(filename);
			if(nodes == null || nodes.Count == 0 || nodes[0].nodeType != Node.NodeType.Title)
			{
				Debug.LogError($"Conversation file must start with a title. | {filename}");
				return;
			}
			
			foreach(Node node in nodes)
			{
				//Debug.Log($"{node.ID} | {node.Depth} | {node.nodeType} | {node.Text}");
				switch (node.nodeType)
				{
					case Node.NodeType.Title:
						conversation.Title = node.Text;
						if(targetDatabase.conversations.Exists(x => x.Title == node.Text))
						{
							conversation.id = targetDatabase.GetConversation(node.Text).id;
							targetDatabase.conversations.RemoveAll(x => x.Title == node.Text);
						}
						break;
					case Node.NodeType.Conversant:
						conversation.ConversantID = GetOrCreateActor(node.Actor).id;
						startNode.ConversantID = conversation.ConversantID;
						break;
					case Node.NodeType.Reply:
					case Node.NodeType.Speak:
						DialogueEntry newEntry = _template.CreateDialogueEntry(node.ID, conversation.id, node.Title);
						newEntry.ActorID = GetOrCreateActor(node.Actor).id;
						if(node.nodeType == Node.NodeType.Speak)
						{
							newEntry.ConversantID = targetDatabase.playerID;
						}
						else
						{
							newEntry.ConversantID = conversation.ConversantID;
						}
						newEntry.DialogueText = node.Text;
						newEntry.userScript = node.Script;
						newEntry.conditionsString = node.Condition;
						newEntry.Sequence = node.Sequence;
						
						if (node.Parent == null)
						{
							//Debug.Log($"{node.ID} | Parent null");
							startNode.outgoingLinks.Add(new(conversation.id, startNode.id, conversation.id, node.ID));
						}
						else
						{
							DialogueEntry parent = conversation.dialogueEntries.Find(x => x.id == node.Parent.ID);
							if (parent != null)
							{
								parent.outgoingLinks.Add(new(conversation.id, node.Parent.ID, conversation.id, node.ID));
							}
						}
						
						
						
						foreach((int convo, int id) links in node.LinksTo)
						{
							int convo = conversation.id;
							if(links.convo != -1)
							{
								convo = links.convo;
							}
							newEntry.outgoingLinks.Add(new(conversation.id, newEntry.id, convo, links.id));
						}

						foreach (string link in node.TitleLinks)
						{
							DialogueEntry target = conversation.GetDialogueEntry(link);
							if (target != null)
							{
								newEntry.outgoingLinks.Add(new(conversation.id, newEntry.id, target.conversationID, target.id));
							}
							else
							{
								linkWhenDone.Add((link, newEntry));
							}
						}
						
						conversation.dialogueEntries.Add(newEntry);
						break;
					case Node.NodeType.Group:
						DialogueEntry newGroup = _template.CreateDialogueEntry(node.ID, conversation.id, node.Title);
						newGroup.ActorID = targetDatabase.playerID;
						newGroup.ConversantID = conversation.ConversantID;
						newGroup.isGroup = true;
						newGroup.userScript = node.Script;
						newGroup.conditionsString = node.Condition;
						newGroup.Sequence = node.Sequence;
						
						if(!string.IsNullOrWhiteSpace(node.Text))
						{
							newGroup.Title = node.Text;
						}
						
						if (node.Parent == null)
						{
							//Debug.Log($"{node.ID} | Parent null");
							startNode.outgoingLinks.Add(new(conversation.id, startNode.id, conversation.id, node.ID));
						}
						else
						{
							DialogueEntry parent = conversation.dialogueEntries.Find(x => x.id == node.Parent.ID);
							if (parent != null)
							{
								parent.outgoingLinks.Add(new(conversation.id, node.Parent.ID, conversation.id, node.ID));
							}
						}

						foreach ((int convo, int id) links in node.LinksTo)
						{
							int convo = conversation.id;
							if (links.convo != -1)
							{
								convo = links.convo;
							}
							newGroup.outgoingLinks.Add(new(conversation.id, newGroup.id, convo, links.id));
						}

						foreach (string link in node.TitleLinks)
						{
							DialogueEntry target = conversation.GetDialogueEntry(link);
							if (target != null)
							{
								newGroup.outgoingLinks.Add(new(conversation.id, newGroup.id, target.conversationID, target.id));
							}
							else
							{
								linkWhenDone.Add((link, newGroup));
							}
						}
						
						conversation.dialogueEntries.Add(newGroup);
						break;
					case Node.NodeType.SequenceNode:
						DialogueEntry newSequenceEntry = _template.CreateDialogueEntry(node.ID, conversation.id, "Sequence");
						newSequenceEntry.ActorID = targetDatabase.playerID;
						newSequenceEntry.ConversantID = conversation.ConversantID;
						newSequenceEntry.Sequence = node.Text;
						newSequenceEntry.userScript = node.Script;
						newSequenceEntry.conditionsString = node.Condition;
						
						if(!string.IsNullOrWhiteSpace(_heldTitle))
						{
							newSequenceEntry.Title = _heldTitle;
						}
						
						if (node.Parent == null)
						{
							//Debug.Log($"{node.ID} | Parent null");
							startNode.outgoingLinks.Add(new(conversation.id, startNode.id, conversation.id, node.ID));
						}
						else
						{
							DialogueEntry parent = conversation.dialogueEntries.Find(x => x.id == node.Parent.ID);
							if (parent != null)
							{
								parent.outgoingLinks.Add(new(conversation.id, node.Parent.ID, conversation.id, node.ID));
							}
						}

						foreach ((int convo, int id) links in node.LinksTo)
						{
							int convo = conversation.id;
							if (links.convo != -1)
							{
								convo = links.convo;
							}
							newSequenceEntry.outgoingLinks.Add(new(conversation.id, newSequenceEntry.id, convo, links.id));
						}

						foreach (string link in node.TitleLinks)
						{
							DialogueEntry target = conversation.GetDialogueEntry(link);
							if (target != null)
							{
								newSequenceEntry.outgoingLinks.Add(new(conversation.id, newSequenceEntry.id, target.conversationID, target.id));
							}
							else
							{
								linkWhenDone.Add((link, newSequenceEntry));
							}
						}
						conversation.dialogueEntries.Add(newSequenceEntry);
						break;
				}
				
			}

			Debug.Log($"Adding Conversation | {conversation.Title}");
			targetDatabase.conversations.Add(conversation);
		}
	}

	private List<Node> Parse(string filename)
	{
		Node root = null;
		Node current = null;
		Node previous = null;
		List<Node> nodes = new();


		elements = File.ReadAllLines(filename).Where(arg => !string.IsNullOrWhiteSpace(arg)).ToList();
		
		if(elements.Count == 0 || !elements[0].StartsWith("title:"))
		{
			Debug.LogError("Conversation file must start with a title.");
			return null;
		}

		for (_elementIndex = 0; _elementIndex < elements.Count; _elementIndex++)
		{
			if (elements[_elementIndex].Trim() == "---" || elements[_elementIndex].Trim() == "===")
			{
				continue;
			}
			if (root == null)
			{
				root = GetAsElementNode(elements[_elementIndex]);
				current = root;
				root.ID = 0;
			}
			else
			{
				Node check = GetAsElementNode(elements[_elementIndex]);
				if (check == null)
				{
					Debug.Log(elements[_elementIndex]);
					continue;
				}

				switch (check.nodeType)
				{
					case Node.NodeType.Script:
						_heldScript = check.Text;
						continue;
					case Node.NodeType.Condition:
						_heldCondition = check.Text;
						continue;
					case Node.NodeType.Sequence:
						_heldSequence = check.Text;
						continue;
					case Node.NodeType.SetNodeTitle:
						_heldTitle = check.Text;
						continue;
					case Node.NodeType.LinkTo:
						_heldLinks.AddRange(check.LinksTo);
						continue;	
					case Node.NodeType.LinkTitle:
						_heldTitleLinks.Add(check.Text);
						continue;
				}

				previous = current;
				current = check;
				
				if(!string.IsNullOrWhiteSpace(_heldCondition))
				{
					current.Condition = _heldCondition;
					_heldCondition = "";
				}
				if (!string.IsNullOrWhiteSpace(_heldScript))
				{
					current.Script = _heldScript;
					_heldScript = "";
				}
				if (!string.IsNullOrWhiteSpace(_heldTitle) && current.nodeType != Node.NodeType.Group)
				{
					current.Title = _heldTitle;
					_heldTitle = "";
				}
				if (!string.IsNullOrWhiteSpace(_heldSequence) && current.nodeType != Node.NodeType.SequenceNode)
				{
					current.Sequence = _heldSequence;
					_heldSequence = "";
				}
				if(_heldLinks?.Count > 0)
				{
					current.LinksTo.AddRange(_heldLinks);
					_heldLinks?.Clear();
				}
				if (_heldTitleLinks?.Count > 0)
				{
					current.TitleLinks.AddRange(_heldTitleLinks);
					_heldTitleLinks?.Clear();
				}
				

				if (!nodes.Exists(x => x.ID == nodes.Count))
				{
					current.ID = nodes.Count;
				}
				else
				{
					current.ID = nodes.Max(x => x.ID) + 1;
				}
				
				if (current.Depth >= previous.Depth)
				{
					//Debug.Log($"Parent set to previous | {current.ID} | {previous.ID} | Depth: {current.Depth}");
					if (previous.nodeType == Node.NodeType.Title || previous.nodeType == Node.NodeType.Conversant)
					{
						current.Parent = null;
					}
					else
					{
						current.Parent = previous;
					}

				}
				else
				{
					Node previousSibling = nodes.LastOrDefault(sibling => sibling.Depth == current.Depth);
					current.Parent = previousSibling;

					if (previousSibling == null)
					{
						//Debug.Log($"Parent set to null | {current.ID} | Depth: {current.Depth}");
						current.Parent = null;
					}
					else
					{
						//Debug.Log($"Parent set to previous sibling parent | {current.ID} | {previousSibling.Parent?.ID} | Depth: {current.Depth}");
						current.Parent = previousSibling.Parent;
					}
				}
			}

			nodes.Add(current);
		}
		return nodes;
	}

	public Actor GetOrCreateActor(string name, bool isPlayer = false, int id = -1)
	{
		var actor = targetDatabase.GetActor(name);
		if (actor == null)
		{
			id = id < 1 ? _template.GetNextActorID(targetDatabase) : id;
			actor = _template.CreateActor(id, name, isPlayer);
			targetDatabase.actors.Add(actor);
		}

		return actor;
	}
	
	
	public Node GetAsElementNode(string element)
	{
		string name = Regex.Match(element, "[a-zA-Z0-9]+").Value;
		string link = Regex.Match(element, "\t+").Value;
		string trimmedElement = element.Trim();

		Node newNode = new(name, link.Length);

		if (element.StartsWith("title:"))
		{
			newNode.nodeType = Node.NodeType.Title;
			newNode.Text = element.Replace("title:", "").Trim();
			return newNode;

		}
		if (element.StartsWith("npc:"))
		{
			newNode.nodeType = Node.NodeType.Conversant;
			newNode.Actor = element.Replace("npc:", "").Trim();
			return newNode;
		}
		
		if(trimmedElement.StartsWith("<<script"))
		{
			newNode.nodeType = Node.NodeType.Script;
			string nodeText = "";
			while(!elements[_elementIndex].Contains(">>"))
			{
				nodeText += elements[_elementIndex] + Environment.NewLine;
				_elementIndex++;
			}
			nodeText += elements[_elementIndex];
			nodeText = nodeText.Replace("<<script", "").Replace(">>", "").Replace("\t", "").Trim();
			newNode.Text = nodeText;
			return newNode;
		}

		if (trimmedElement.StartsWith(value: "<<cond"))
		{
			newNode.nodeType = Node.NodeType.Condition;
			string nodeText = "";
			while (!elements[_elementIndex].Contains(">>"))
			{
				nodeText += elements[_elementIndex] + Environment.NewLine;
				_elementIndex++;
			}
			nodeText += elements[_elementIndex];
			nodeText = nodeText.Replace("<<cond", "").Replace(">>", "").Replace("\t", "").Trim();
			newNode.Text = nodeText;
			return newNode;
		}

		if (trimmedElement.StartsWith(value: "<<title"))
		{
			newNode.nodeType = Node.NodeType.SetNodeTitle;
			newNode.Text = element.Replace("<<title", "").Replace(">>", "").Trim();
			return newNode;
		}

		if (trimmedElement.StartsWith(value: "<<link"))
		{
			newNode.nodeType = Node.NodeType.LinkTo;
			string[] links = element.Replace("<<link", "").Replace(">>", "").Trim().Split(" ");
			if (links.Length == 1)
			{
				if (int.TryParse(links[0], out int nodeID))
				{
					newNode.LinksTo.Add((-1, nodeID));
				}
				else
				{
					newNode.nodeType = Node.NodeType.LinkTitle;
					newNode.Text = element.Replace("<<link", "").Replace(">>", "").Trim();
				}
			}
			else if (links.Length > 1)
			{
				if (int.TryParse(links[0], out int convoID)
				&& int.TryParse(links[1], out int nodeID))
				{
					newNode.LinksTo.Add((convoID, nodeID));
				}
				else
				{
					newNode.nodeType = Node.NodeType.LinkTitle;
					newNode.Text = element.Replace("<<link", "").Replace(">>", "").Trim();
				}
			}
			else
			{
				Debug.LogError($"LinkTo Improperly Formatted | {trimmedElement}");
				return null;
			}
			return newNode;
		}

		if (trimmedElement.StartsWith(value: "<<group"))
		{
			newNode.nodeType = Node.NodeType.Group;
			newNode.Title = element.Replace("<<group", "").Replace(">>", "").Trim();
			return newNode;
		}

		if (trimmedElement.StartsWith("<<seqnode"))
		{
			newNode.nodeType = Node.NodeType.SequenceNode;
			string nodeText = "";
			while (!elements[_elementIndex].Contains(">>"))
			{
				nodeText += elements[_elementIndex] + Environment.NewLine;
				_elementIndex++;
			}
			nodeText += elements[_elementIndex];
			nodeText = nodeText.Replace("<<seqnode", "").Replace(">>", "").Replace("\t", "").Trim();
			newNode.Text = nodeText;
			return newNode;
		}

		if (trimmedElement.StartsWith("<<seq"))
		{
			newNode.nodeType = Node.NodeType.Sequence;
			string nodeText = "";
			while (!elements[_elementIndex].Contains(">>"))
			{
				nodeText += elements[_elementIndex] + Environment.NewLine;
				_elementIndex++;
			}
			nodeText += elements[_elementIndex];
			nodeText = nodeText.Replace("<<seq", "").Replace(">>", "").Replace("\t", "").Trim();
			newNode.Text = nodeText;
			return newNode;
		}

		Match speakerMatch = Regex.Match(element, "^(.+?):");
		if (speakerMatch.Success)
		{
			newNode.nodeType = Node.NodeType.Speak;
			newNode.Actor = speakerMatch.Value.Replace(":", "").Trim();
			newNode.Text = element.Replace(speakerMatch.Value, "").Trim().Replace("\n", Environment.NewLine);
			return newNode;
		}

		Match replyMatch = Regex.Match(element, "^.+?->");
		if (replyMatch.Success)
		{
			newNode.nodeType = Node.NodeType.Reply;
			newNode.Actor = "Player";
			newNode.Text = element.Replace("->", "").Trim().Replace("\n", Environment.NewLine);
			return newNode;
		}

		return null;
	}

	public class Node
	{
		public int ID { get; set; }
		public string Name { get; set; }
		public int Depth { get; set; }
		public Node Parent { get; set; }
		public string Title { get; set; }
		public string Text { get; set; }
		public string Actor { get; set; }
		public string Script { get; set; }
		public string Condition { get; set; }
		public string Sequence { get; set; }
		public List<string> TitleLinks = new();

		public List<(int convo, int id)> LinksTo = new();
		public NodeType nodeType = NodeType.Title;

		public enum NodeType { Title, Conversant, Speak, Reply, Script, Condition, Sequence, SequenceNode, LinkTo, Group, SetNodeTitle, LinkTitle }

		public Node(string name, int depth)
		{
			Name = name;
			Depth = depth;
		}
	}
}
