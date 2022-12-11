# DSTextImporter
An importer for DSText format files with Dialogue System for Unity

These are plaintext files inspired by YarnSpinner's syntax. It expects one conversation per file and will overwrite conversations with the same name in the target database.

Indents must be tabs. Empty lines are allowed and ignored.

To Install: Put it in an Editor Folder in a Unity project with Dialogue System for Unity by Pixel Crushers installed.

## Syntax Overview
```
[HEADER]
title: Sets conversation title
npc: Sets main conversation NPC

[DIALOGUE]
CharacterName: Dialogue
-> Player Replies or Dialogue

[CODE BITS]
seqnode: Add a standalone sequence node.
group: Adds a group node with the given title.
seq: Adds a Sequence to the next dialogue line or group.
script: Adds a Script to the next dialogue line, sequence node, or group.
cond: Adds a Condition to the next dialogue line sequence node, or group.
title: Sets the title of the next dialogue line sequence node, or group.
link: Links to either a title or a node id. 
      Works across all conversations in a database. 
      Applies to next dialogue line, group, or sequence node.
```

## Sample
```
title: Tester/Testy
npc: Alice
---
<<script 
	Test();
>>
<<title Opening>>
Alice: Testing
	-> Reply
		<<script AddMore()>>
		<<cond Tester == false;>>
		<<seq None()>>
		Alice: Testing Reply
			<<group 999>>
				-> Testing another Reply
					<<seqnode ANodeSequence();
					OnMultipleLines>>
					Alice: I love dialogues!
					Alice: I am a humdinger.
					-> Player talks again.
				-> Well now what's all this?
					<<group Target>>
						Alice: I can reply to your replies too!
						<<link Opening>>
						Alice: I am muddafugga.
Alice: Testing 2
```
