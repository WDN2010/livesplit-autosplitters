using System;
using System.Collections.Generic;
using Peppered;

public static class ReaderTests
{
    private const long Scene = 0x1000;
    private const long ChoicesStatic = 0x1008;
    private const long PrisonStatic = 0x1010;
    private const long ChoicesObject = 0x2000;
    private const long PrisonObject = 0x2100;
    private const long ChoicesEntries = 0x3000;
    private const long PrisonEntries = 0x3100;
    private const long Keys = 0x5000;
    private const long CheckpointObject = 0x2200;
    private const long DialogueStatic = 0x6000;
    private const long DialogueObject = 0x7000;
    private const long DialogueLines = 0x8000;
    private const long DialogueLine = 0x9000;
    private const long DialogueTextBox = 0xA000;
    private const long TvDialogueClass = 0x1601;
    private const long SurrenderObject = 0xA100;
    private const long SurrenderChooser = 0xA200;
    private const long SurrenderLines = 0xA300;
    private const long SurrenderLine = 0xA400;
    private const long SurrenderLeft = 0xA500;
    private const long SurrenderRight = 0xA600;
    private const long StartManager = 0xB000;
    private const long StartPlayer = 0xC000;
    private const long TheodoreSelectables = 0xD000;
    private const long TheodoreObject = 0xE000;
    private const long TheodoreNavigation = 0xE100;
    private const long TheodoreFill = 0xE200;
    private const long TheodoreVtable = 0xE300;
    private const long TheodoreFillVtable = 0xE400;
    // Production DialogueManager/TMP tuple from the pinned Windows Mono build.
    private const int DialoguePlayingOffset = 196;
    private const int DialogueLinesOffset = 96;
    private const int DialogueCurrentLineOffset = 188;
    private const int DialogueTextBoxOffset = 32;
    private const int DialogueVisibleOffset = 1268;
    private const int StartCutsceneOffset = 32;
    private const int StartInstanceOffset = 40;
    private const int StartPlayerOffset = 32;
    private const int StartCanMoveOffset = 40;
    private const int EndingCutsceneOffset = 32;
    private const int EndingDeadStateOffset = 36;
    private const int TheodoreSelectablesOffset = 40;
    private const int TheodoreSelectableCountOffset = 48;
    private const int EntryStride = 24;
    private static int passed;

    private sealed class FakeMemory : ReadOnlyReader.IReaderMemory
    {
        private readonly Dictionary<long, int> ints = new Dictionary<long, int>();
        private readonly Dictionary<long, long> pointers = new Dictionary<long, long>();
        private readonly Dictionary<long, bool> bools = new Dictionary<long, bool>();
        private readonly Dictionary<long, string> strings = new Dictionary<long, string>();
        private readonly HashSet<long> failedInts = new HashSet<long>();
        private readonly HashSet<long> failedPointers = new HashSet<long>();
        internal Action<FakeMemory> AfterHashRead;
        internal Action<FakeMemory> AfterAbyssRead;
        internal Action<FakeMemory> AfterCheckpointRead;
        internal Action<FakeMemory> AfterDialoguePointerRead;
        internal Action<FakeMemory, long> AfterDialoguePayloadRead;
        internal int CheckpointPointerReads;
        internal int DialoguePointerReads;
        internal List<long> ReadAddresses = new List<long>();


        internal void Int(long address, int value) { ints[address] = value; }
        internal void Pointer(long address, long value) { pointers[address] = value; }
        internal void Bool(long address, bool value) { bools[address] = value; }
        internal void String(long address, string value) { strings[address] = value; }
        internal void Float(long address, float value)
        {
            ints[address] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }
        internal void FailInt(long address) { failedInts.Add(address); }
        internal void RecoverInt(long address) { failedInts.Remove(address); }
        internal void FailPointer(long address) { failedPointers.Add(address); }

        public bool TryReadInt(IntPtr address, out int value)
        {
            value = 0;
            long raw = address.ToInt64();
            ReadAddresses.Add(raw);
            if (failedInts.Contains(raw) || !ints.TryGetValue(raw, out value))
                return false;
            if (raw == Scene)
            {
                Action<FakeMemory> callback = AfterAbyssRead;
                if (callback != null)
                {
                    AfterAbyssRead = null;
                    callback(this);
                }
            }
            if (raw == ChoicesEntries + 32 || raw == PrisonEntries + 32)
            {
                Action<FakeMemory> callback = AfterHashRead;
                if (callback != null)
                {
                    AfterHashRead = null;
                    callback(this);
                }
            }
            Action<FakeMemory, long> payloadCallback = AfterDialoguePayloadRead;
            if (payloadCallback != null)
            {
                AfterDialoguePayloadRead = null;
                payloadCallback(this, raw);
            }
            return true;
        }

        public bool TryReadBool(IntPtr address, out bool value)
        {
            long raw = address.ToInt64();
            ReadAddresses.Add(raw);
            bool ok = bools.TryGetValue(raw, out value);
            Action<FakeMemory, long> payloadCallback = AfterDialoguePayloadRead;
            if (ok && payloadCallback != null)
            {
                AfterDialoguePayloadRead = null;
                payloadCallback(this, raw);
            }
            return ok;
        }

        public bool TryReadPointer(IntPtr address, out IntPtr value)
        {
            long raw = address.ToInt64();
            long pointerValue;
            ReadAddresses.Add(raw);
            if (failedPointers.Contains(raw) || !pointers.TryGetValue(raw, out pointerValue))
            {
                value = IntPtr.Zero;
                return false;
            }
            value = new IntPtr(pointerValue);
            if (address.ToInt64() == Scene + 24)
            {
                CheckpointPointerReads++;
                Action<FakeMemory> callback = AfterCheckpointRead;
                if (callback != null)
                {
                    AfterCheckpointRead = null;
                    callback(this);
                }
            }
            if (address.ToInt64() == DialogueStatic)
            {
                DialoguePointerReads++;
                Action<FakeMemory> callback = AfterDialoguePointerRead;
                if (callback != null)
                {
                    AfterDialoguePointerRead = null;
                    callback(this);
                }
            }
            Action<FakeMemory, long> payloadCallback = AfterDialoguePayloadRead;
            if (payloadCallback != null)
            {
                AfterDialoguePayloadRead = null;
                payloadCallback(this, raw);
            }
            return true;
        }

        public bool TryReadString(IntPtr address, out string value)
        {
            long raw = address.ToInt64();
            ReadAddresses.Add(raw);
            value = null;
            bool ok = strings.TryGetValue(raw, out value);
            Action<FakeMemory, long> payloadCallback = AfterDialoguePayloadRead;
            if (ok && payloadCallback != null)
            {
                AfterDialoguePayloadRead = null;
                payloadCallback(this, raw);
            }
            return ok;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed++;
    }

    private static ReadOnlyReader Reader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16));
    }

    private static ReadOnlyReader CheckpointReader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), 24);
    }

    private static ReadOnlyReader DialogueReader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1,
            ReadOnlyReader.TestDialogueLayout(new IntPtr(DialogueStatic), 0,
                DialoguePlayingOffset));
    }

    private static ReadOnlyReader FullDialogueReader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1,
            ReadOnlyReader.TestDialogueLayout(new IntPtr(DialogueStatic), 0,
                DialoguePlayingOffset, DialogueLinesOffset, DialogueCurrentLineOffset,
                DialogueTextBoxOffset, DialogueVisibleOffset));
    }

    private static ReadOnlyReader TvStartReader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1,
            null, null, null, null, null,
            ReadOnlyReader.TestTvStartLayout(new IntPtr(DialogueStatic), 0,
                new IntPtr(TvDialogueClass), DialogueLinesOffset,
                DialogueCurrentLineOffset, DialoguePlayingOffset));
    }

    private static ReadOnlyReader SurrenderReader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1,
            null, null, null, null,
            ReadOnlyReader.TestSurrenderLayout(new IntPtr(DialogueStatic), 0));
    }

    private static ReadOnlyReader KarminaReader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1,
            null, null, null, null,
            ReadOnlyReader.TestSurrenderLayout(new IntPtr(DialogueStatic), 0));
    }

    private static void PutSurrender(FakeMemory memory, long dialogueObject,
        long chooserObject, string line, int choice, bool playing = true,
        bool texting = false, int currentLine = 0, int lineCount = 1,
        string left = "B_M/m41", string right = "B_M/m42")
    {
        memory.Int(Scene, 600);
        memory.Pointer(DialogueStatic, dialogueObject);
        memory.Pointer(dialogueObject, 0x11020);
        memory.Pointer(dialogueObject + 40, chooserObject);
        memory.Pointer(chooserObject, 0x11021);
        memory.Int(chooserObject + 32, choice);
        memory.Pointer(dialogueObject + 96, SurrenderLines);
        memory.Int(SurrenderLines + 24, lineCount);
        if (lineCount > 0)
        {
            memory.Pointer(SurrenderLines + 32, SurrenderLine);
            memory.String(SurrenderLine, line);
        }
        memory.Int(dialogueObject + 188, currentLine);
        memory.Pointer(dialogueObject + 80, SurrenderLeft);
        memory.String(SurrenderLeft, left);
        memory.Pointer(dialogueObject + 88, SurrenderRight);
        memory.String(SurrenderRight, right);
        memory.Bool(dialogueObject + 196, playing);
        memory.Bool(dialogueObject + 200, texting);
    }

    private static void PutSurrender(FakeMemory memory, string line, int choice,
        bool playing = true, bool texting = false, int currentLine = 0,
        int lineCount = 1, string left = "B_M/m41", string right = "B_M/m42")
    {
        PutSurrender(memory, SurrenderObject, SurrenderChooser, line, choice,
            playing, texting, currentLine, lineCount, left, right);
    }

    private static ReadOnlyReader StartReader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1, null,
            ReadOnlyReader.TestStartLayout(new IntPtr(Scene), StartCutsceneOffset,
                StartInstanceOffset, StartPlayerOffset, StartCanMoveOffset));
    }

    private static ReadOnlyReader EndingReader(FakeMemory memory)
    {
        return EndingReader(memory,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16),
            ReadOnlyReader.TestEndingLayout(new IntPtr(Scene), EndingCutsceneOffset,
                EndingDeadStateOffset));
    }

    private static ReadOnlyReader EndingReader(FakeMemory memory,
        ReadOnlyReader.DictionaryLayout choices,
        ReadOnlyReader.DictionaryLayout collectibles,
        ReadOnlyReader.EndingLayout ending)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            choices, collectibles, -1, null, null, ending);
    }

    private static ReadOnlyReader TheodoreReader(FakeMemory memory)
    {
        return ReadOnlyReader.ForTests(memory, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1, null, null,
            ReadOnlyReader.TestEndingLayout(new IntPtr(Scene), EndingCutsceneOffset,
                EndingDeadStateOffset),
            ReadOnlyReader.TestTheodoreLayout(new IntPtr(Scene), TheodoreSelectablesOffset,
                TheodoreSelectableCountOffset));
    }

    private static void PutTheodore(FakeMemory memory, int hp, bool cutscene,
        bool qualified = true, bool native = true, int sliderCount = 1)
    {
        memory.Int(Scene, 300);
        memory.Bool(Scene + EndingCutsceneOffset, cutscene);
        memory.Int(Scene + EndingDeadStateOffset, 0);
        memory.Pointer(Scene + TheodoreSelectablesOffset, TheodoreSelectables);
        memory.Int(Scene + TheodoreSelectableCountOffset, sliderCount);
        memory.Int(TheodoreSelectables + 24, sliderCount);
        memory.Pointer(TheodoreSelectables + 32, TheodoreObject);
        memory.Pointer(TheodoreObject + 0, TheodoreVtable);
        memory.Pointer(TheodoreVtable + 0, qualified ? 0x1003 : 0x1BAD);
        memory.Pointer(TheodoreObject + 16, native ? 0x8000 : 0);
        memory.Int(TheodoreObject + 24, 0);
        memory.Pointer(TheodoreObject + 104, 0);
        memory.Int(TheodoreObject + 220, 0);
        memory.Pointer(TheodoreObject + 232, TheodoreFill);
        memory.Pointer(TheodoreObject + 240, 0);
        memory.Int(TheodoreObject + 296, 0);
        memory.Float(TheodoreObject + 300, 0.0f);
        memory.Float(TheodoreObject + 304, 100.0f);
        memory.Bool(TheodoreObject + 308, true);
        memory.Float(TheodoreObject + 312, hp);
        memory.Pointer(TheodoreFill + 0, TheodoreFillVtable);
        memory.Pointer(TheodoreFillVtable + 0, 0x1006);
        memory.Pointer(TheodoreFill + 16, 0x8100);
        if (sliderCount > 1)
            memory.Pointer(TheodoreSelectables + 32 + 8, TheodoreObject + 0x100);
    }

    private static void PutDialogue(FakeMemory memory, string line, bool playing,
        int currentLine, int visible, int length)
    {
        memory.Pointer(DialogueStatic, DialogueObject);
        memory.Bool(DialogueObject + DialoguePlayingOffset, playing);
        memory.Pointer(DialogueObject + DialogueLinesOffset, DialogueLines);
        memory.Int(DialogueLines + 24, length);
        memory.Pointer(DialogueLines + 32, DialogueLine);
        memory.Int(DialogueObject + DialogueCurrentLineOffset, currentLine);
        memory.String(DialogueLine, line);
        memory.Pointer(DialogueObject + DialogueTextBoxOffset, DialogueTextBox);
        memory.Int(DialogueTextBox + DialogueVisibleOffset, visible);
    }

    private static void PutTvStart(FakeMemory memory, string line,
        bool playing = true, int currentLine = 0, int lineCount = 1)
    {
        memory.Int(Scene, 63);
        memory.Pointer(DialogueStatic, DialogueObject);
        // TestTvStartLayout uses the same direct class-identity escape hatch as
        // the existing surrender fixture; production reads use vtable->class.
        memory.Pointer(DialogueObject, TvDialogueClass);
        memory.Pointer(DialogueObject + DialogueLinesOffset, DialogueLines);
        memory.Int(DialogueLines + 24, lineCount);
        if (lineCount > 0)
        {
            memory.Pointer(DialogueLines + 32, DialogueLine);
            memory.String(DialogueLine, line);
        }
        memory.Int(DialogueObject + DialogueCurrentLineOffset, currentLine);
        memory.Bool(DialogueObject + DialoguePlayingOffset, playing);
    }

    private static void PutDictionary(FakeMemory memory, long staticAddress,
        long dictionary, long entries, int count, int version, int capacity)
    {
        memory.Pointer(staticAddress, dictionary);
        memory.Int(dictionary + 64, count);
        memory.Int(dictionary + 76, version);
        memory.Pointer(dictionary + 24, entries);
        if (entries != 0) memory.Int(entries + 24, capacity);
    }

    private static void PutEntry(FakeMemory memory, long entries, int index,
        int hashCode, long key, string text, int next)
    {
        long entry = entries + 32 + index * EntryStride;
        memory.Int(entry, hashCode);
        memory.Int(entry + 4, next);
        memory.Pointer(entry + 8, key);
        memory.Int(key + 16, text.Length);
        memory.String(key, text);
    }

    private static void PutEntryValue(FakeMemory memory, long entries, int index, int value)
    {
        memory.Int(entries + 32 + index * EntryStride + 16, value);
    }

    private static void TestEmptyAndDeletedHoles()
    {
        FakeMemory memory = new FakeMemory();
        memory.Int(Scene, 17);
        PutDictionary(memory, ChoicesStatic, ChoicesObject, ChoicesEntries, 3, 1, 4);
        PutEntry(memory, ChoicesEntries, 0, -1, 0, "deleted", -1);
        PutEntry(memory, ChoicesEntries, 1, 3, Keys, "Other", -1);
        PutEntry(memory, ChoicesEntries, 2, 4, Keys + 0x100, "Larry", -1);
        ReadResult result = Reader(memory).Read("B_6.6");
        Check(result.Valid && result.Abyss == 17, "deleted holes retain integer validity");
        Check(result.Bigman.HasValue && result.Bigman.Value, "present marker survives deleted holes");

        FakeMemory empty = new FakeMemory();
        empty.Int(Scene, 18);
        PutDictionary(empty, ChoicesStatic, ChoicesObject, 0, 0, 2, 0);
        ReadResult emptyResult = Reader(empty).Read("B_6.6");
        Check(emptyResult.Valid && emptyResult.Bigman.HasValue && !emptyResult.Bigman.Value,
            "allocated empty dictionary is false");
    }

    private static void TestAbsentAndNullAreDifferent()
    {
        FakeMemory absent = new FakeMemory();
        absent.Int(Scene, 19);
        PutDictionary(absent, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 3, 1);
        PutEntry(absent, ChoicesEntries, 0, 1, Keys, "Other", -1);
        ReadResult absentResult = Reader(absent).Read("B_6.6");
        Check(absentResult.Valid && absentResult.Bigman.HasValue && !absentResult.Bigman.Value,
            "valid dictionary without marker is false");

        FakeMemory nullDictionary = new FakeMemory();
        nullDictionary.Int(Scene, 20);
        nullDictionary.Pointer(ChoicesStatic, 0);
        ReadResult nullResult = Reader(nullDictionary).Read("B_6.6");
        Check(nullResult.Valid && !nullResult.Bigman.HasValue,
            "null dictionary remains null rather than false");
    }

    private static void TestPrisonAndSceneGating()
    {
        FakeMemory memory = new FakeMemory();
        memory.Int(Scene, 21);
        PutDictionary(memory, PrisonStatic, PrisonObject, PrisonEntries, 1, 4, 1);
        PutEntry(memory, PrisonEntries, 0, 1, Keys + 0x200, "MerdekaFriend", -1);
        ReadResult prison = Reader(memory).Read("A_12_Prison");
        Check(prison.Valid && prison.Prison.HasValue && prison.Prison.Value,
            "prison marker is read only in prison scene");
        ReadResult otherScene = Reader(memory).Read("A_13_The_Court");
        Check(otherScene.Valid && !otherScene.Prison.HasValue && !otherScene.Bigman.HasValue,
            "optional dictionaries are not read in unrelated scenes");
    }

    private static void TestBoundsAndMutation()
    {
        FakeMemory oversizedCount = new FakeMemory();
        oversizedCount.Int(Scene, 22);
        PutDictionary(oversizedCount, ChoicesStatic, ChoicesObject, ChoicesEntries, 4097, 5, 4097);
        ReadResult countResult = Reader(oversizedCount).Read("B_6.6");
        Check(countResult.Valid && !countResult.Bigman.HasValue &&
            countResult.Diagnostic.IndexOf("bigman-unavailable", StringComparison.Ordinal) >= 0,
            "oversized count fails closed without breaking Abyss");

        FakeMemory oversizedCapacity = new FakeMemory();
        oversizedCapacity.Int(Scene, 23);
        PutDictionary(oversizedCapacity, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 5, 8193);
        PutEntry(oversizedCapacity, ChoicesEntries, 0, 1, Keys, "Larry", -1);
        ReadResult capacityResult = Reader(oversizedCapacity).Read("B_6.6");
        Check(capacityResult.Valid && !capacityResult.Bigman.HasValue,
            "oversized capacity fails closed");

        FakeMemory mutated = new FakeMemory();
        mutated.Int(Scene, 24);
        PutDictionary(mutated, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 6, 1);
        PutEntry(mutated, ChoicesEntries, 0, 1, Keys, "Larry", -1);
        mutated.AfterHashRead = delegate(FakeMemory m) { m.Int(ChoicesObject + 76, 7); };
        ReadResult mutationResult = Reader(mutated).Read("B_6.6");
        Check(mutationResult.Valid && !mutationResult.Bigman.HasValue &&
            mutationResult.Diagnostic.IndexOf("bigman-mutated", StringComparison.Ordinal) >= 0,
            "dictionary mutation is rejected");
    }

    private static void TestReadFailuresAndAbyssRecheck()
    {
        FakeMemory optionalFailure = new FakeMemory();
        optionalFailure.Int(Scene, 25);
        PutDictionary(optionalFailure, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 8, 1);
        PutEntry(optionalFailure, ChoicesEntries, 0, 1, Keys, "Larry", -1);
        optionalFailure.FailInt(Keys + 16);
        ReadResult optionalResult = Reader(optionalFailure).Read("B_6.6");
        Check(optionalResult.Valid && !optionalResult.Bigman.HasValue &&
            (optionalResult.Diagnostic.IndexOf("bigman-read-failed", StringComparison.Ordinal) >= 0 ||
             optionalResult.Diagnostic.IndexOf("bigman-unavailable", StringComparison.Ordinal) >= 0),
            "optional read failure preserves valid Abyss");

        FakeMemory abyssFailure = new FakeMemory();
        abyssFailure.FailInt(Scene);
        ReadResult invalid = Reader(abyssFailure).Read("B_6.6");
        Check(!invalid.Valid && !invalid.Bigman.HasValue &&
            invalid.Diagnostic.IndexOf("abyss-read-failed", StringComparison.Ordinal) >= 0,
            "Abyss read failure invalidates the sample");
    }

    private static void TestCheckpointSignal()
    {
        FakeMemory cleared = new FakeMemory();
        cleared.Int(Scene, 26);
        cleared.Pointer(Scene + 24, 0);
        ReadResult clearedResult = CheckpointReader(cleared).Read("Office_1");
        Check(clearedResult.Valid && clearedResult.HasCheckpoint.HasValue
            && !clearedResult.HasCheckpoint.Value, "null checkpoint means new game");

        FakeMemory present = new FakeMemory();
        present.Int(Scene, 27);
        present.Pointer(Scene + 24, CheckpointObject);
        present.Int(CheckpointObject + 16, 8);
        present.String(CheckpointObject, "Office_1");
        ReadResult presentResult = CheckpointReader(present).Read("Office_1");
        Check(presentResult.Valid && presentResult.HasCheckpoint.HasValue
            && presentResult.HasCheckpoint.Value, "well-formed checkpoint means loaded game");

        FakeMemory unknown = new FakeMemory();
        unknown.Int(Scene, 28);
        ReadResult unknownResult = CheckpointReader(unknown).Read("Office_1");
        Check(unknownResult.Valid && !unknownResult.HasCheckpoint.HasValue
            && unknownResult.Diagnostic.IndexOf("checkpoint-read-failed", StringComparison.Ordinal) >= 0,
            "checkpoint pointer failure remains unknown");

        FakeMemory malformed = new FakeMemory();
        malformed.Int(Scene, 29);
        malformed.Pointer(Scene + 24, CheckpointObject);
        malformed.Int(CheckpointObject + 16, 8);
        ReadResult malformedResult = CheckpointReader(malformed).Read("Office_1");
        Check(malformedResult.Valid && !malformedResult.HasCheckpoint.HasValue
            && malformedResult.Diagnostic.IndexOf("checkpoint-malformed", StringComparison.Ordinal) >= 0,
            "malformed checkpoint remains unknown");

        FakeMemory nullToPresent = new FakeMemory();
        nullToPresent.Int(Scene, 30);
        nullToPresent.Pointer(Scene + 24, 0);
        nullToPresent.AfterCheckpointRead = delegate(FakeMemory m) {
            m.Pointer(Scene + 24, CheckpointObject);
        };
        ReadResult nullToPresentResult = CheckpointReader(nullToPresent).Read("Office_1");
        Check(nullToPresentResult.Valid && !nullToPresentResult.HasCheckpoint.HasValue
            && nullToPresentResult.Diagnostic.IndexOf("checkpoint-mutated", StringComparison.Ordinal) >= 0,
            "null checkpoint changing after first read remains unknown");

        FakeMemory presentToNull = new FakeMemory();
        presentToNull.Int(Scene, 31);
        presentToNull.Pointer(Scene + 24, CheckpointObject);
        presentToNull.Int(CheckpointObject + 16, 8);
        presentToNull.String(CheckpointObject, "Office_1");
        presentToNull.AfterCheckpointRead = delegate(FakeMemory m) {
            m.Pointer(Scene + 24, 0);
        };
        ReadResult presentToNullResult = CheckpointReader(presentToNull).Read("Office_1");
        Check(presentToNullResult.Valid && !presentToNullResult.HasCheckpoint.HasValue
            && presentToNullResult.Diagnostic.IndexOf("checkpoint-mutated", StringComparison.Ordinal) >= 0,
            "non-null checkpoint changing after decode remains unknown");

        FakeMemory secondReadFailure = new FakeMemory();
        secondReadFailure.Int(Scene, 32);
        secondReadFailure.Pointer(Scene + 24, 0);
        secondReadFailure.AfterCheckpointRead = delegate(FakeMemory m) {
            m.FailPointer(Scene + 24);
        };
        ReadResult secondReadFailureResult = CheckpointReader(secondReadFailure).Read("Office_1");
        Check(secondReadFailureResult.Valid && !secondReadFailureResult.HasCheckpoint.HasValue
            && secondReadFailureResult.Diagnostic.IndexOf("checkpoint-read-failed", StringComparison.Ordinal) >= 0,
            "checkpoint second read failure remains unknown");
    }

    private static void TestMalformedOffsets()
    {
        FakeMemory core = new FakeMemory();
        core.Int(Scene, 33);
        ReadOnlyReader badCore = ReadOnlyReader.ForTests(core, new IntPtr(Scene), Int32.MaxValue,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16));
        ReadResult coreResult = badCore.Read("Office_1");
        Check(!coreResult.Valid && !core.ReadAddresses.Contains(Scene + Int32.MaxValue),
            "int.MaxValue core offset is rejected before remote read");

        FakeMemory checkpoint = new FakeMemory();
        checkpoint.Int(Scene, 34);
        ReadOnlyReader badCheckpoint = ReadOnlyReader.ForTests(checkpoint, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), Int32.MaxValue);
        ReadResult checkpointResult = badCheckpoint.Read("Office_1");
        Check(checkpointResult.Valid && !checkpointResult.HasCheckpoint.HasValue
            && !checkpoint.ReadAddresses.Contains(Scene + Int32.MaxValue),
            "int.MaxValue checkpoint offset remains unknown without remote read");

        FakeMemory dictionary = new FakeMemory();
        dictionary.Int(Scene, 35);
        PutDictionary(dictionary, ChoicesStatic, ChoicesObject, 0, 0, 1, 0);
        ReadOnlyReader.DictionaryLayout badLayout = ReadOnlyReader.TestDictionaryLayout(
            new IntPtr(Scene), 8);
        badLayout.EntriesOffset = Int32.MaxValue;
        ReadResult dictionaryResult = ReadOnlyReader.ForTests(dictionary, new IntPtr(Scene), 0,
            badLayout, ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16)).Read("B_6.6");
        Check(dictionaryResult.Valid && !dictionaryResult.Bigman.HasValue
            && !dictionary.ReadAddresses.Contains(ChoicesObject + Int32.MaxValue),
            "int.MaxValue dictionary offset is rejected before remote read");
    }

    private static void TestQualifiedDictionaryShape()
    {
        FakeMemory oldHeader = new FakeMemory();
        oldHeader.Int(Scene, 57);
        PutDictionary(oldHeader, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(oldHeader, ChoicesEntries, 0, 1, Keys, "Larry", -1);
        ReadOnlyReader.DictionaryLayout oldLayout = ReadOnlyReader.TestDictionaryLayout(
            new IntPtr(Scene), 8);
        oldLayout.EntriesOffset = 16;
        oldLayout.CountOffset = 24;
        oldLayout.VersionOffset = 28;
        ReadResult oldResult = ReadOnlyReader.ForTests(oldHeader, new IntPtr(Scene), 0,
            oldLayout, ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16))
            .Read("B_6.6");
        Check(oldResult.Valid && !oldResult.Bigman.HasValue
            && !oldHeader.ReadAddresses.Contains(ChoicesObject + 64),
            "pre-qualified dictionary header shape is rejected");

        FakeMemory boxedRow = new FakeMemory();
        boxedRow.Int(Scene, 58);
        PutDictionary(boxedRow, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(boxedRow, ChoicesEntries, 0, 1, Keys, "Larry", -1);
        ReadOnlyReader.DictionaryLayout boxedLayout = ReadOnlyReader.TestDictionaryLayout(
            new IntPtr(Scene), 8);
        boxedLayout.HashCodeOffset = 16;
        boxedLayout.NextOffset = 20;
        boxedLayout.KeyOffset = 24;
        boxedLayout.ValueOffset = 32;
        ReadResult boxedResult = ReadOnlyReader.ForTests(boxedRow, new IntPtr(Scene), 0,
            boxedLayout, ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16))
            .Read("B_6.6");
        Check(boxedResult.Valid && !boxedResult.Bigman.HasValue
            && !boxedRow.ReadAddresses.Contains(ChoicesEntries + 32 + 16),
            "boxed Entry offsets are not accepted by normalized test reader");

        ReadOnlyReader.DictionaryLayout wrongStride = ReadOnlyReader.TestDictionaryLayout(
            new IntPtr(Scene), 8);
        wrongStride.EntryStride = 28;
        FakeMemory stride = new FakeMemory();
        stride.Int(Scene, 59);
        PutDictionary(stride, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        ReadResult strideResult = ReadOnlyReader.ForTests(stride, new IntPtr(Scene), 0,
            wrongStride, ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16))
            .Read("B_6.6");
        Check(strideResult.Valid && !strideResult.Bigman.HasValue,
            "self-consistent nonqualified Entry stride is rejected");
    }

    private static void TestDialogueSignal()
    {
        FakeMemory state = new FakeMemory();
        state.Int(Scene, 36);
        state.Pointer(DialogueStatic, DialogueObject);
        state.Bool(DialogueObject + DialoguePlayingOffset, false);
        ReadResult before = DialogueReader(state).Read("Subspace_Final");
        Check(before.Valid && before.DialoguePlaying == false,
            "Subspace_Final dialogue false is observed exactly");

        state.Bool(DialogueObject + DialoguePlayingOffset, true);
        ReadResult started = DialogueReader(state).Read("Subspace_Final");
        Check(started.Valid && started.DialoguePlaying == true,
            "Subspace_Final dialogue true is observed exactly");

        FakeMemory otherScene = new FakeMemory();
        otherScene.Int(Scene, 37);
        ReadResult other = DialogueReader(otherScene).Read("B_5");
        Check(other.Valid && !other.DialoguePlaying.HasValue
            && !otherScene.ReadAddresses.Contains(DialogueStatic),
            "dialogue instance is not read outside Subspace_Final");

        FakeMemory nullInstance = new FakeMemory();
        nullInstance.Int(Scene, 38);
        nullInstance.Pointer(DialogueStatic, 0);
        ReadResult missing = DialogueReader(nullInstance).Read("Subspace_Final");
        Check(missing.Valid && !missing.DialoguePlaying.HasValue
            && missing.Diagnostic.IndexOf("dialogue-instance-unavailable", StringComparison.Ordinal) >= 0,
            "null dialogue instance remains unknown");

        FakeMemory pointerFailure = new FakeMemory();
        pointerFailure.Int(Scene, 40);
        pointerFailure.FailPointer(DialogueStatic);
        ReadResult pointerUnknown = DialogueReader(pointerFailure).Read("Subspace_Final");
        Check(pointerUnknown.Valid && !pointerUnknown.DialoguePlaying.HasValue
            && pointerUnknown.Diagnostic.IndexOf("dialogue-instance-read-failed", StringComparison.Ordinal) >= 0,
            "dialogue instance pointer failure remains unknown");

        FakeMemory boolFailure = new FakeMemory();
        boolFailure.Int(Scene, 41);
        boolFailure.Pointer(DialogueStatic, DialogueObject);
        ReadResult boolUnknown = DialogueReader(boolFailure).Read("Subspace_Final");
        Check(boolUnknown.Valid && !boolUnknown.DialoguePlaying.HasValue
            && boolUnknown.Diagnostic.IndexOf("dialogue-playing-read-failed", StringComparison.Ordinal) >= 0,
            "dialogue playing read failure remains unknown");

        FakeMemory badOffset = new FakeMemory();
        badOffset.Int(Scene, 42);
        badOffset.Pointer(DialogueStatic, DialogueObject);
        ReadOnlyReader invalidLayout = ReadOnlyReader.ForTests(badOffset, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1,
            ReadOnlyReader.TestDialogueLayout(new IntPtr(DialogueStatic), 0, Int32.MaxValue));
        ReadResult badOffsetResult = invalidLayout.Read("Subspace_Final");
        Check(badOffsetResult.Valid && !badOffsetResult.DialoguePlaying.HasValue
            && !badOffset.ReadAddresses.Contains(DialogueObject + Int32.MaxValue),
            "absurd dialogue offset is rejected before remote read");

        FakeMemory mutated = new FakeMemory();
        mutated.Int(Scene, 39);
        mutated.Pointer(DialogueStatic, DialogueObject);
        mutated.Bool(DialogueObject + DialoguePlayingOffset, true);
        mutated.AfterDialoguePointerRead = delegate(FakeMemory m) {
            m.Pointer(DialogueStatic, DialogueObject + 0x100);
        };
        ReadResult changed = DialogueReader(mutated).Read("Subspace_Final");
        Check(changed.Valid && !changed.DialoguePlaying.HasValue
            && changed.Diagnostic.IndexOf("dialogue-mutated", StringComparison.Ordinal) >= 0,
            "changed dialogue instance pointer remains unknown");
    }

    private static void TestManagedBoundaryReaders()
    {
        FakeMemory barMemory = new FakeMemory();
        barMemory.Int(Scene, 43);
        PutDialogue(barMemory, "S_L/m20", true, 0, 1, 1);
        ReadResult bar = FullDialogueReader(barMemory).Read("S_4");
        Check(bar.Valid && bar.BoundaryMetadataReady && bar.DialoguePlaying == true
            && bar.EndingSignals.Contains("ending.bar_dialogue")
            && bar.EndingSignals.Count == 1, "S_4 Unsung Hero first bar dialogue key");

        string[] barLines = { "S_L/m21", "S_L/m22", "S_L/m24", "B_E/m1", "C_End/m47", "C_End/m61", "C_End/m69" };
        for (int i = 0; i < barLines.Length; i++)
        {
            FakeMemory branch = new FakeMemory();
            branch.Int(Scene, 44 + i);
            PutDialogue(branch, barLines[i], true, 0, 1, 1);
            ReadResult branchResult = FullDialogueReader(branch).Read("S_4");
            Check(!branchResult.EndingSignals.Contains("ending.bar_dialogue"),
                "reject later/unrelated bar line " + barLines[i]);
        }

        foreach (string wrongScene in new string[] { "B_End", "C_End", "S_3" })
        {
            Check(!FullDialogueReader(barMemory).Read(wrongScene).EndingSignals.Contains("ending.bar_dialogue"),
                "Unsung Hero payload cannot finish outside S_4: " + wrongScene);
        }

        FakeMemory tv = new FakeMemory();
        tv.Int(Scene, 46);
        PutDialogue(tv, "B_A/m36", true, 0, 1, 1);
        ReadResult tvResult = FullDialogueReader(tv).Read("B_Aftermath");
        Check(tvResult.Valid && !tvResult.EndingSignals.Contains("ending.karmina_tv"),
            "B_Aftermath TV dash is not an active ending");

        FakeMemory gem = new FakeMemory();
        gem.Int(Scene, 47);
        PutDialogue(gem, "Gem/m11", true, 0, 1, 1);
        ReadResult gemResult = FullDialogueReader(gem).Read("Subspace_Final");
        Check(gemResult.EndingSignals.Contains("ending.gem_dialogue"),
            "Subspace_Final first GEM dialogue key");

        FakeMemory early = new FakeMemory();
        early.Int(Scene, 48);
        PutDialogue(early, "Gem/m11", true, 0, 0, 1);
        Check(FullDialogueReader(early).Read("Subspace_Final").EndingSignals.Count == 0,
            "visible zero rejects dialogue boundary");
        early.Int(DialogueTextBox + DialogueVisibleOffset, 1);
        PutDialogue(early, "Gem/m11", false, 0, 1, 1);
        Check(FullDialogueReader(early).Read("Subspace_Final").EndingSignals.Count == 0,
            "Playing false rejects dialogue boundary");
        PutDialogue(early, "Gem/m11", true, 1, 1, 1);
        Check(FullDialogueReader(early).Read("Subspace_Final").EndingSignals.Count == 0,
            "later dialogue index rejects first boundary");
        PutDialogue(early, "Gem/m14", true, 0, 1, 1);
        Check(FullDialogueReader(early).Read("Subspace_Final").EndingSignals.Count == 0,
            "later dialogue payload rejects first boundary");

        FakeMemory arrayBounds = new FakeMemory();
        arrayBounds.Int(Scene, 49);
        PutDialogue(arrayBounds, "S_L/m20", true, 0, 1, 0);
        Check(FullDialogueReader(arrayBounds).Read("S_4").EndingSignals.Count == 0,
            "empty managed string array fails closed");
        PutDialogue(arrayBounds, "S_L/m20", true, 0, 1, 4097);
        Check(FullDialogueReader(arrayBounds).Read("S_4").EndingSignals.Count == 0,
            "oversized managed string array fails closed");

        FakeMemory arrayRace = new FakeMemory();
        arrayRace.Int(Scene, 50);
        PutDialogue(arrayRace, "S_L/m20", true, 0, 1, 1);
        Action<FakeMemory, long> arrayMutation = null;
        arrayMutation = delegate(FakeMemory m, long address) {
            if (address == DialogueObject + DialogueLinesOffset)
                m.Pointer(DialogueObject + DialogueLinesOffset, DialogueLines + 0x100);
            else
                m.AfterDialoguePayloadRead = arrayMutation;
        };
        arrayRace.AfterDialoguePayloadRead = arrayMutation;
        ReadResult arrayRaceResult = FullDialogueReader(arrayRace).Read("S_4");
        Check(arrayRaceResult.Valid && arrayRaceResult.EndingSignals.Count == 0
            && (arrayRaceResult.Diagnostic.IndexOf("dialogue-mutated", StringComparison.Ordinal) >= 0
                || arrayRaceResult.Diagnostic.IndexOf("dialogue-reread-failed", StringComparison.Ordinal) >= 0),
            "DILines pointer race fails closed");

        FakeMemory indexRace = new FakeMemory();
        indexRace.Int(Scene, 51);
        PutDialogue(indexRace, "S_L/m20", true, 0, 1, 1);
        Action<FakeMemory, long> indexMutation = null;
        indexMutation = delegate(FakeMemory m, long address) {
            if (address == DialogueObject + DialogueCurrentLineOffset)
                m.Int(DialogueObject + DialogueCurrentLineOffset, 1);
            else
                m.AfterDialoguePayloadRead = indexMutation;
        };
        indexRace.AfterDialoguePayloadRead = indexMutation;
        ReadResult indexRaceResult = FullDialogueReader(indexRace).Read("S_4");
        Check(indexRaceResult.Valid && indexRaceResult.EndingSignals.Count == 0
            && indexRaceResult.Diagnostic.IndexOf("dialogue-mutated", StringComparison.Ordinal) >= 0,
            "CurrentLineIndex race fails closed");

        FakeMemory stringRace = new FakeMemory();
        stringRace.Int(Scene, 52);
        PutDialogue(stringRace, "S_L/m20", true, 0, 1, 1);
        Action<FakeMemory, long> stringMutation = null;
        stringMutation = delegate(FakeMemory m, long address) {
            if (address == DialogueLine)
                m.String(DialogueLine, "S_L/m21");
            else
                m.AfterDialoguePayloadRead = stringMutation;
        };
        stringRace.AfterDialoguePayloadRead = stringMutation;
        ReadResult stringRaceResult = FullDialogueReader(stringRace).Read("S_4");
        Check(stringRaceResult.Valid && stringRaceResult.EndingSignals.Count == 0
            && stringRaceResult.Diagnostic.IndexOf("dialogue-mutated", StringComparison.Ordinal) >= 0,
            "managed dialogue string race fails closed");

        FakeMemory visibleRace = new FakeMemory();
        visibleRace.Int(Scene, 53);
        PutDialogue(visibleRace, "S_L/m20", true, 0, 1, 1);
        Action<FakeMemory, long> visibleMutation = null;
        visibleMutation = delegate(FakeMemory m, long address) {
            if (address == DialogueTextBox + DialogueVisibleOffset)
                m.Int(DialogueTextBox + DialogueVisibleOffset, 0);
            else
                m.AfterDialoguePayloadRead = visibleMutation;
        };
        visibleRace.AfterDialoguePayloadRead = visibleMutation;
        ReadResult visibleRaceResult = FullDialogueReader(visibleRace).Read("S_4");
        Check(visibleRaceResult.Valid && visibleRaceResult.EndingSignals.Count == 0
            && visibleRaceResult.Diagnostic.IndexOf("dialogue-mutated", StringComparison.Ordinal) >= 0,
            "visible-character race fails closed");

        foreach (int invalidVisibleOffset in new int[] { 1264, 1272, -4, 1266, Int32.MaxValue })
        {
            FakeMemory invalidOffset = new FakeMemory();
            invalidOffset.Int(Scene, 54 + invalidVisibleOffset);
            PutDialogue(invalidOffset, "S_L/m20", true, 0, 1, 1);
            ReadOnlyReader invalidReader = ReadOnlyReader.ForTests(invalidOffset,
                new IntPtr(Scene), 0,
                ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
                ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1,
                ReadOnlyReader.TestDialogueLayout(new IntPtr(DialogueStatic), 0,
                    DialoguePlayingOffset, DialogueLinesOffset, DialogueCurrentLineOffset,
                    DialogueTextBoxOffset, invalidVisibleOffset));
            ReadResult invalidResult = invalidReader.Read("S_4");
            Check(invalidResult.Valid && !invalidResult.BoundaryMetadataReady
                && invalidResult.EndingSignals.Count == 0
                && !invalidOffset.ReadAddresses.Contains(DialogueTextBox + invalidVisibleOffset),
                "nonqualified TMP visibility offset fails closed: " + invalidVisibleOffset);
        }

        FakeMemory unrelated = new FakeMemory();
        unrelated.Int(Scene, 53);
        PutDialogue(unrelated, "S_L/m20", true, 0, 1, 1);
        ReadResult unrelatedResult = FullDialogueReader(unrelated).Read("B_5");
        Check(unrelatedResult.Valid && !unrelatedResult.BoundaryMetadataReady
            && unrelatedResult.EndingSignals.Count == 0
            && !unrelated.ReadAddresses.Contains(DialogueStatic),
            "dialogue metadata is not read in unrelated scene");
    }

    private static void TestTvStartReader()
    {
        FakeMemory activeMemory = new FakeMemory();
        PutTvStart(activeMemory, "B_A/m36", true, 0, 1);
        ReadResult active = TvStartReader(activeMemory).Read("B_Aftermath");
        Check(active.Valid && active.BoundaryMetadataReady
            && active.TvStartMetadataReady && active.TvStartActive == true
            && active.EndingSignals.Contains("ending.karmina_tv_start"),
            "B_Aftermath exact m36 managed producer emits TV-start");
        Check(!activeMemory.ReadAddresses.Contains(DialogueTextBox)
            && !activeMemory.ReadAddresses.Contains(DialogueObject + DialogueTextBoxOffset)
            && !activeMemory.ReadAddresses.Contains(DialogueObject + 40),
            "TV-start reader has no TMP, glyph, or chooser dependency");

        string[] wrongLines = { "B_A/m1", "B_A/m7", "B_A/m33", "B_A/m37" };
        for (int i = 0; i < wrongLines.Length; i++)
        {
            FakeMemory wrong = new FakeMemory();
            PutTvStart(wrong, wrongLines[i], true, 0, 1);
            ReadResult result = TvStartReader(wrong).Read("B_Aftermath");
            Check(result.Valid && result.TvStartActive == false
                && result.EndingSignals.Count == 0,
                "TV-start rejects unrelated/camera line " + wrongLines[i]);
        }

        FakeMemory notPlaying = new FakeMemory();
        PutTvStart(notPlaying, "B_A/m36", false, 0, 1);
        ReadResult notPlayingResult = TvStartReader(notPlaying).Read("B_Aftermath");
        Check(notPlayingResult.Valid && notPlayingResult.TvStartActive == false
            && notPlayingResult.EndingSignals.Count == 0,
            "post-dialogue m36 with Playing false is inactive");

        FakeMemory wrongIndex = new FakeMemory();
        PutTvStart(wrongIndex, "B_A/m36", true, 1, 1);
        ReadResult wrongIndexResult = TvStartReader(wrongIndex).Read("B_Aftermath");
        Check(wrongIndexResult.Valid && wrongIndexResult.TvStartActive == false
            && wrongIndexResult.EndingSignals.Count == 0,
            "TV-start requires line index zero");

        FakeMemory wrongLength = new FakeMemory();
        PutTvStart(wrongLength, "B_A/m36", true, 0, 2);
        ReadResult wrongLengthResult = TvStartReader(wrongLength).Read("B_Aftermath");
        Check(wrongLengthResult.Valid && wrongLengthResult.TvStartActive == false
            && wrongLengthResult.EndingSignals.Count == 0,
            "TV-start requires the producer's single-line array");

        FakeMemory empty = new FakeMemory();
        PutTvStart(empty, null, false, 0, 0);
        ReadResult emptyResult = TvStartReader(empty).Read("B_Aftermath");
        Check(emptyResult.Valid && emptyResult.TvStartActive == false
            && emptyResult.EndingSignals.Count == 0,
            "empty dialogue array is valid inactive TV state");

        FakeMemory wrongScene = new FakeMemory();
        PutTvStart(wrongScene, "B_A/m36", true, 0, 1);
        ReadResult wrongSceneResult = TvStartReader(wrongScene).Read("B_5");
        Check(wrongSceneResult.Valid && wrongSceneResult.TvStartActive == null
            && !wrongSceneResult.BoundaryMetadataReady
            && !wrongScene.ReadAddresses.Contains(DialogueStatic),
            "TV-start is not read outside B_Aftermath");

        FakeMemory unstableBase = new FakeMemory();
        PutTvStart(unstableBase, "B_A/m36", true, 0, 1);
        unstableBase.AfterAbyssRead = delegate(FakeMemory m) { m.Int(Scene, 64); };
        ReadResult unstableBaseResult = TvStartReader(unstableBase).Read("B_Aftermath");
        Check(!unstableBaseResult.Valid
            && !unstableBaseResult.EndingSignals.Contains("ending.karmina_tv_start"),
            "unstable Abyss base cannot emit TV-start");

        FakeMemory unstablePayload = new FakeMemory();
        PutTvStart(unstablePayload, "B_A/m36", true, 0, 1);
        Action<FakeMemory, long> payloadRace = null;
        payloadRace = delegate(FakeMemory m, long address) {
            if (address == DialogueObject + DialogueLinesOffset)
                m.Pointer(DialogueObject + DialogueLinesOffset, DialogueLines + 0x100);
            else
                m.AfterDialoguePayloadRead = payloadRace;
        };
        unstablePayload.AfterDialoguePayloadRead = payloadRace;
        ReadResult unstablePayloadResult = TvStartReader(unstablePayload).Read("B_Aftermath");
        Check(unstablePayloadResult.Valid && unstablePayloadResult.TvStartActive == null
            && unstablePayloadResult.EndingSignals.Count == 0,
            "mutated TV payload fails closed without a retained binding");
    }

    private static void TestSurrenderChoiceReader()
    {
        FakeMemory stale = new FakeMemory();
        PutSurrender(stale, "B_M/m45", 1);
        ReadResult staleResult = SurrenderReader(stale).Read("B_Merdeka");
        Check(staleResult.Valid && staleResult.BoundaryMetadataReady
            && staleResult.SurrenderMetadataReady && !staleResult.EndingSignals.Contains("ending.merdeka_surrender"),
            "initial choice one cannot arm Ending8");

        FakeMemory memory = new FakeMemory();
        PutSurrender(memory, "B_M/m45", 0);
        ReadOnlyReader reader = SurrenderReader(memory);
        ReadResult pending = reader.Read("B_Merdeka");
        Check(pending.Valid && pending.SurrenderChoice == 0
            && pending.SurrenderPhase == "pending"
            && pending.EndingSignals.Count == 0,
            "stable final prompt choice zero arms provisional surrender");
        PutSurrender(memory, "B_M/m45", 1, false);
        ReadResult confirmed = reader.Read("B_Merdeka");
        Check(confirmed.Valid && confirmed.SurrenderChoice == 1
            && confirmed.SurrenderPhase == "confirmed"
            && confirmed.EndingSignals.Contains("ending.merdeka_surrender"),
            "choice one confirms after closed StartChoice");
        Check(reader.Read("B_Merdeka").EndingSignals.Count == 0,
            "confirmed surrender does not repeat");

        FakeMemory rejected = new FakeMemory();
        PutSurrender(rejected, "B_M/m45", 0);
        ReadOnlyReader rejectReader = SurrenderReader(rejected);
        rejectReader.Read("B_Merdeka");
        PutSurrender(rejected, "B_M/m45", 2, false);
        Check(rejectReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "choice two rejects and disarms surrender");
        PutSurrender(rejected, "B_M/m45", 1, false);
        Check(rejectReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "rejected surrender cannot replay on choice one");

        foreach (string wrongLine in new string[] { "B_M/m44", "B_M/m46" })
        {
            FakeMemory wrong = new FakeMemory();
            PutSurrender(wrong, wrongLine, 0);
            ReadResult wrongResult = SurrenderReader(wrong).Read("B_Merdeka");
            Check(wrongResult.Valid && wrongResult.EndingSignals.Count == 0,
                "wrong dialogue line cannot arm surrender: " + wrongLine);
        }
        FakeMemory wrongIndex = new FakeMemory();
        PutSurrender(wrongIndex, "B_M/m45", 0, true, false, 1);
        Check(SurrenderReader(wrongIndex).Read("B_Merdeka").EndingSignals.Count == 0,
            "wrong dialogue index cannot arm surrender");
        FakeMemory wrongArray = new FakeMemory();
        PutSurrender(wrongArray, "B_M/m45", 0, true, false, 0, 2);
        Check(SurrenderReader(wrongArray).Read("B_Merdeka").EndingSignals.Count == 0,
            "wrong dialogue array length cannot arm surrender");
        FakeMemory wrongStrings = new FakeMemory();
        PutSurrender(wrongStrings, "B_M/m45", 0, true, false, 0, 1,
            "B_M/m40", "B_M/m42");
        Check(SurrenderReader(wrongStrings).Read("B_Merdeka").EndingSignals.Count == 0,
            "wrong choice strings cannot arm surrender");

        FakeMemory invalidGap = new FakeMemory();
        PutSurrender(invalidGap, "B_M/m45", 0);
        ReadOnlyReader gapReader = SurrenderReader(invalidGap);
        gapReader.Read("B_Merdeka");
        invalidGap.FailInt(Scene);
        Check(!gapReader.Read("B_Merdeka").Valid,
            "Abyss failure invalidates the surrender poll");
        invalidGap.RecoverInt(Scene);
        PutSurrender(invalidGap, "B_M/m45", 1, false);
        Check(gapReader.Read("B_Merdeka").EndingSignals.Contains("ending.merdeka_surrender"),
            "pure invalid same-identity gap retains and revalidates surrender arm");

        FakeMemory unstable = new FakeMemory();
        PutSurrender(unstable, "B_M/m45", 0);
        unstable.AfterDialoguePayloadRead = delegate(FakeMemory m, long address) {
            m.Int(Scene, 601);
        };
        ReadOnlyReader unstableReader = SurrenderReader(unstable);
        Check(!unstableReader.Read("B_Merdeka").Valid,
            "Abyss mutation cannot commit a new surrender arm");
        unstable.Int(Scene, 600);
        PutSurrender(unstable, "B_M/m45", 1, false);
        Check(unstableReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "unstable initial poll cannot retro-confirm surrender");

        FakeMemory identity = new FakeMemory();
        PutSurrender(identity, "B_M/m45", 0);
        ReadOnlyReader identityReader = SurrenderReader(identity);
        identityReader.Read("B_Merdeka");
        PutSurrender(identity, SurrenderObject + 0x100, SurrenderChooser,
            "B_M/m45", 1, false);
        Check(identityReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "DialogueManager identity change clears surrender arm");

        FakeMemory chooserIdentity = new FakeMemory();
        PutSurrender(chooserIdentity, "B_M/m45", 0);
        ReadOnlyReader chooserReader = SurrenderReader(chooserIdentity);
        chooserReader.Read("B_Merdeka");
        PutSurrender(chooserIdentity, SurrenderObject, SurrenderChooser + 0x100,
            "B_M/m45", 1, false);
        Check(chooserReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "Button_Choose identity change clears surrender arm");

        FakeMemory reset = new FakeMemory();
        PutSurrender(reset, "B_M/m45", 0);
        ReadOnlyReader resetReader = SurrenderReader(reset);
        resetReader.Read("B_Merdeka");
        resetReader.ResetSurrenderBinding();
        PutSurrender(reset, "B_M/m45", 1, false);
        Check(resetReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "ResetSurrenderBinding clears only new surrender arm");

        FakeMemory scene = new FakeMemory();
        PutSurrender(scene, "B_M/m45", 0);
        ReadOnlyReader sceneReader = SurrenderReader(scene);
        sceneReader.Read("B_Merdeka");
        PutSurrender(scene, "B_M/m45", 1, false);
        Check(sceneReader.Read("B_5").EndingSignals.Count == 0,
            "surrender reader does not emit outside B_Merdeka");
        Check(sceneReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "scene exit clears surrender arm");
    }

    private static void TestManagedStartReader()
    {
        FakeMemory ready = new FakeMemory();
        ready.Int(Scene, 54);
        ready.Bool(Scene + StartCutsceneOffset, false);
        ready.Pointer(Scene + StartInstanceOffset, StartManager);
        ready.Pointer(StartManager + StartPlayerOffset, StartPlayer);
        ready.Bool(StartPlayer + StartCanMoveOffset, true);
        ReadResult readyResult = StartReader(ready).Read("Office_1");
        Check(readyResult.Valid && readyResult.BoundaryMetadataReady
            && readyResult.StartReady == true, "Office_1 control-ready start");

        ready.Bool(Scene + StartCutsceneOffset, true);
        Check(StartReader(ready).Read("Office_1").StartReady == false,
            "cutscene gate blocks start");
        ready.Bool(Scene + StartCutsceneOffset, false);
        ready.Bool(StartPlayer + StartCanMoveOffset, false);
        Check(StartReader(ready).Read("Office_1").StartReady == false,
            "CanMove gate blocks start");

        FakeMemory missingPlayer = new FakeMemory();
        missingPlayer.Int(Scene, 55);
        missingPlayer.Bool(Scene + StartCutsceneOffset, false);
        missingPlayer.Pointer(Scene + StartInstanceOffset, StartManager);
        missingPlayer.Pointer(StartManager + StartPlayerOffset, 0);
        ReadResult missingPlayerResult = StartReader(missingPlayer).Read("Office_1");
        Check(missingPlayerResult.Valid && !missingPlayerResult.StartReady.HasValue,
            "missing player keeps start unknown");

        FakeMemory changed = new FakeMemory();
        changed.Int(Scene, 56);
        changed.Bool(Scene + StartCutsceneOffset, false);
        changed.Pointer(Scene + StartInstanceOffset, StartManager);
        changed.Pointer(StartManager + StartPlayerOffset, StartPlayer);
        changed.Bool(StartPlayer + StartCanMoveOffset, true);
        Action<FakeMemory, long> startMutation = null;
        startMutation = delegate(FakeMemory m, long address) {
            if (address == Scene + StartInstanceOffset)
                m.Pointer(Scene + StartInstanceOffset, StartManager + 0x100);
            else
                m.AfterDialoguePayloadRead = startMutation;
        };
        changed.AfterDialoguePayloadRead = startMutation;
        ReadResult changedResult = StartReader(changed).Read("Office_1");
        Check(changedResult.Valid && !changedResult.StartReady.HasValue
            && changedResult.Diagnostic.IndexOf("start-mutated", StringComparison.Ordinal) >= 0,
            "start singleton pointer race fails closed");

        ready.ReadAddresses.Clear();
        ReadResult other = StartReader(ready).Read("C_End");
        Check(other.Valid && !other.StartReady.HasValue
            && !ready.ReadAddresses.Contains(Scene + StartCutsceneOffset),
            "start metadata is not read outside Office_1");

        FakeMemory badOffset = new FakeMemory();
        badOffset.Int(Scene, 57);
        ReadOnlyReader.StartLayout badStart = ReadOnlyReader.TestStartLayout(new IntPtr(Scene),
            Int32.MaxValue, StartInstanceOffset, StartPlayerOffset, StartCanMoveOffset);
        ReadResult badOffsetResult = ReadOnlyReader.ForTests(badOffset, new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16), -1, null, badStart)
            .Read("Office_1");
        Check(badOffsetResult.Valid && !badOffsetResult.BoundaryMetadataReady
            && !badOffset.ReadAddresses.Contains(Scene + Int32.MaxValue),
            "absurd start offset is rejected before remote read");
    }

    private static void TestKarminaFinalChoiceReader()
    {
        FakeMemory stale = new FakeMemory();
        PutSurrender(stale, "B_E/m161", 1, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadResult staleResult = KarminaReader(stale).Read("B_End");
        Check(staleResult.Valid && staleResult.BoundaryMetadataReady
            && staleResult.KarminaMetadataReady && staleResult.KarminaChoice == 1
            && !staleResult.EndingSignals.Contains("ending.karmina_final_choice"),
            "initial Karmina choice one cannot retro-finish");

        FakeMemory continueChoice = new FakeMemory();
        PutSurrender(continueChoice, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadOnlyReader reader = KarminaReader(continueChoice);
        ReadResult pending = reader.Read("B_End");
        Check(pending.Valid && pending.BoundaryMetadataReady
            && pending.KarminaChoice == 0 && pending.KarminaPhase == "pending"
            && pending.EndingSignals.Count == 0,
            "stable B_End final prompt choice zero arms Karmina binding");
        PutSurrender(continueChoice, "B_E/m161", 1, false, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadResult confirmedContinue = reader.Read("B_End");
        Check(confirmedContinue.Valid && confirmedContinue.KarminaChoice == 1
            && confirmedContinue.KarminaPhase == "confirmed"
            && confirmedContinue.EndingSignals.Contains("ending.karmina_final_choice"),
            "Karmina CONTINUE choice confirms the physical ending event");
        Check(reader.Read("B_End").EndingSignals.Count == 0,
            "Karmina final choice is one-shot");

        FakeMemory giveUpChoice = new FakeMemory();
        PutSurrender(giveUpChoice, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadOnlyReader giveUpReader = KarminaReader(giveUpChoice);
        giveUpReader.Read("B_End");
        PutSurrender(giveUpChoice, "B_E/m161", 2, false, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadResult confirmedGiveUp = giveUpReader.Read("B_End");
        Check(confirmedGiveUp.Valid && confirmedGiveUp.KarminaChoice == 2
            && confirmedGiveUp.EndingSignals.Contains("ending.karmina_final_choice"),
            "Karmina GIVE UP choice confirms the same physical event");

        FakeMemory rejected = new FakeMemory();
        PutSurrender(rejected, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadOnlyReader rejectReader = KarminaReader(rejected);
        rejectReader.Read("B_End");
        PutSurrender(rejected, "B_E/m161", 3, false, false, 0, 1,
            "B_E/m162", "B_E/m163");
        Check(rejectReader.Read("B_End").EndingSignals.Count == 0,
            "invalid Karmina choice clears the pending binding");
        PutSurrender(rejected, "B_E/m161", 1, false, false, 0, 1,
            "B_E/m162", "B_E/m163");
        Check(rejectReader.Read("B_End").EndingSignals.Count == 0,
            "invalid Karmina choice cannot be replayed");

        foreach (string wrongLine in new string[] { "B_E/m160", "B_E/m162" })
        {
            FakeMemory wrong = new FakeMemory();
            PutSurrender(wrong, wrongLine, 0, true, false, 0, 1,
                "B_E/m162", "B_E/m163");
            Check(KarminaReader(wrong).Read("B_End").EndingSignals.Count == 0,
                "wrong Karmina prompt line cannot arm: " + wrongLine);
        }
        FakeMemory wrongLabels = new FakeMemory();
        PutSurrender(wrongLabels, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m160", "B_E/m163");
        Check(KarminaReader(wrongLabels).Read("B_End").EndingSignals.Count == 0,
            "wrong Karmina choice labels cannot arm");
        FakeMemory wrongIndex = new FakeMemory();
        PutSurrender(wrongIndex, "B_E/m161", 0, true, false, 1, 1,
            "B_E/m162", "B_E/m163");
        Check(KarminaReader(wrongIndex).Read("B_End").EndingSignals.Count == 0,
            "wrong Karmina dialogue index cannot arm");
        FakeMemory wrongTexting = new FakeMemory();
        PutSurrender(wrongTexting, "B_E/m161", 0, true, true, 0, 1,
            "B_E/m162", "B_E/m163");
        Check(KarminaReader(wrongTexting).Read("B_End").EndingSignals.Count == 0,
            "Karmina Texting state cannot arm");

        FakeMemory invalidGap = new FakeMemory();
        PutSurrender(invalidGap, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadOnlyReader gapReader = KarminaReader(invalidGap);
        gapReader.Read("B_End");
        invalidGap.FailInt(Scene);
        Check(!gapReader.Read("B_End").Valid,
            "Abyss failure invalidates the Karmina poll");
        invalidGap.RecoverInt(Scene);
        PutSurrender(invalidGap, "B_E/m161", 2, false, false, 0, 1,
            "B_E/m162", "B_E/m163");
        Check(gapReader.Read("B_End").EndingSignals.Contains("ending.karmina_final_choice"),
            "pure invalid same-identity gap retains Karmina binding");

        FakeMemory unstable = new FakeMemory();
        PutSurrender(unstable, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        unstable.AfterDialoguePayloadRead = delegate(FakeMemory m, long address) {
            m.Int(Scene, 601);
        };
        ReadOnlyReader unstableReader = KarminaReader(unstable);
        Check(!unstableReader.Read("B_End").Valid,
            "Abyss mutation cannot commit a Karmina arm");
        unstable.Int(Scene, 600);
        PutSurrender(unstable, "B_E/m161", 1, false, false, 0, 1,
            "B_E/m162", "B_E/m163");
        Check(unstableReader.Read("B_End").EndingSignals.Count == 0,
            "unstable initial Karmina poll cannot retro-confirm");

        FakeMemory identity = new FakeMemory();
        PutSurrender(identity, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadOnlyReader identityReader = KarminaReader(identity);
        identityReader.Read("B_End");
        PutSurrender(identity, SurrenderObject + 0x100, SurrenderChooser,
            "B_E/m161", 1, false, false, 0, 1, "B_E/m162", "B_E/m163");
        Check(identityReader.Read("B_End").EndingSignals.Count == 0,
            "Karmina DialogueManager identity change clears binding");

        FakeMemory reset = new FakeMemory();
        PutSurrender(reset, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadOnlyReader resetReader = KarminaReader(reset);
        resetReader.Read("B_End");
        resetReader.ResetKarminaBinding();
        PutSurrender(reset, "B_E/m161", 2, false, false, 0, 1,
            "B_E/m162", "B_E/m163");
        Check(resetReader.Read("B_End").EndingSignals.Count == 0,
            "ResetKarminaBinding clears only the Karmina arm");

        FakeMemory scope = new FakeMemory();
        PutSurrender(scope, "B_E/m161", 0, true, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadOnlyReader scopeReader = KarminaReader(scope);
        scopeReader.Read("B_End");
        PutSurrender(scope, "B_E/m161", 1, false, false, 0, 1,
            "B_E/m162", "B_E/m163");
        ReadResult wrongScene = scopeReader.Read("B_Merdeka");
        Check(wrongScene.Valid && !wrongScene.KarminaChoice.HasValue
            && wrongScene.EndingSignals.Count == 0,
            "Karmina reader never emits in B_Merdeka");
        Check(scopeReader.Read("B_End").EndingSignals.Count == 0,
            "leaving B_End clears the Karmina arm");

        FakeMemory missingMetadata = new FakeMemory();
        missingMetadata.Int(Scene, 602);
        ReadOnlyReader missingReader = ReadOnlyReader.ForTests(missingMetadata,
            new IntPtr(Scene), 0,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16));
        ReadResult missing = missingReader.Read("B_End");
        Check(missing.Valid && !missing.BoundaryMetadataReady
            && !missing.KarminaMetadataReady,
            "missing optional Karmina metadata preserves a valid sample");
    }

    private static void TestSceneBoundaryMetadataReadiness()
    {
        string[] scenes = { "T_Boss", "Office_3", "Office_4" };
        foreach (string scene in scenes)
        {
            FakeMemory qualifiedMemory = new FakeMemory();
            qualifiedMemory.Int(Scene, 70);
            ReadResult qualified = EndingReader(qualifiedMemory).Read(scene);
            Check(qualified.Valid && qualified.BoundaryMetadataReady,
                scene + " qualified boundary metadata is ready");

            FakeMemory missingChoicesMemory = new FakeMemory();
            missingChoicesMemory.Int(Scene, 71);
            ReadOnlyReader.DictionaryLayout validCollectibles =
                ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 16);
            ReadOnlyReader.EndingLayout validEnding =
                ReadOnlyReader.TestEndingLayout(new IntPtr(Scene), EndingCutsceneOffset,
                    EndingDeadStateOffset);
            ReadResult missingChoices = EndingReader(missingChoicesMemory, null,
                validCollectibles, validEnding).Read(scene);
            Check(missingChoices.Valid && !missingChoices.BoundaryMetadataReady,
                scene + " missing choices metadata stays unknown");

            FakeMemory malformedMemory = new FakeMemory();
            malformedMemory.Int(Scene, 72);
            ReadOnlyReader.DictionaryLayout malformedChoices =
                ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8);
            malformedChoices.EntryStride = 16;
            ReadResult malformed = EndingReader(malformedMemory, malformedChoices,
                validCollectibles, validEnding).Read(scene);
            Check(malformed.Valid && !malformed.BoundaryMetadataReady,
                scene + " malformed choices metadata fails closed");

            FakeMemory missingCutsceneMemory = new FakeMemory();
            missingCutsceneMemory.Int(Scene, 73);
            ReadOnlyReader.EndingLayout missingCutscene =
                ReadOnlyReader.TestEndingLayout(new IntPtr(Scene), EndingCutsceneOffset,
                    EndingDeadStateOffset);
            missingCutscene.CutscenePlayingField = null;
            ReadResult noCutscene = EndingReader(missingCutsceneMemory,
                ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8),
                validCollectibles, missingCutscene).Read(scene);
            Check(noCutscene.Valid && !noCutscene.BoundaryMetadataReady,
                scene + " missing cutscene metadata stays unknown");
        }

        FakeMemory unrelatedMissing = new FakeMemory();
        unrelatedMissing.Int(Scene, 74);
        ReadResult unrelatedResult = EndingReader(unrelatedMissing,
            ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene), 8), null,
            ReadOnlyReader.TestEndingLayout(new IntPtr(Scene), EndingCutsceneOffset,
                EndingDeadStateOffset)).Read("T_Boss");
        Check(unrelatedResult.Valid && unrelatedResult.BoundaryMetadataReady
            && !unrelatedResult.TheodoreHp.HasValue,
            "T_Boss does not require Theodore HP metadata for boundary readiness");

        FakeMemory absent = new FakeMemory();
        absent.Int(Scene, 75);
        Check(EndingReader(absent).Read("T_Boss").BoundaryMetadataReady,
            "T_Boss readiness ignores absent marker values");

        FakeMemory positive = new FakeMemory();
        positive.Int(Scene, 76);
        positive.Bool(Scene + EndingCutsceneOffset, false);
        PutDictionary(positive, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(positive, ChoicesEntries, 0, 1, Keys, "Batteries", -1);
        PutEntryValue(positive, ChoicesEntries, 0, 2);
        Check(EndingReader(positive).Read("T_Boss").BoundaryMetadataReady,
            "T_Boss readiness ignores positive marker value");

        FakeMemory loss = new FakeMemory();
        loss.Int(Scene, 77);
        loss.Bool(Scene + EndingCutsceneOffset, true);
        PutDictionary(loss, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(loss, ChoicesEntries, 0, 1, Keys, "Batteries", -1);
        PutEntryValue(loss, ChoicesEntries, 0, 0);
        Check(EndingReader(loss).Read("T_Boss").BoundaryMetadataReady,
            "T_Boss readiness ignores loss marker value");

        FakeMemory unrelatedScene = new FakeMemory();
        unrelatedScene.Int(Scene, 78);
        Check(!EndingReader(unrelatedScene).Read("B_5").BoundaryMetadataReady,
            "unlisted scenes do not become boundary-ready");
    }

    private static void TestSourceConfirmedEndingMarkers()
    {
        FakeMemory office3 = new FakeMemory();
        office3.Int(Scene, 60);
        office3.Bool(Scene + EndingCutsceneOffset, true);
        office3.Int(Scene + EndingDeadStateOffset, 0);
        PutDictionary(office3, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(office3, ChoicesEntries, 0, 1, Keys, "OfficeEnd", -1);
        PutEntryValue(office3, ChoicesEntries, 0, 2);
        ReadResult execution = EndingReader(office3).Read("Office_3");
        Check(execution.Valid && execution.EndingSignals.Contains("ending.execution_black")
            && !execution.EndingSignals.Contains("ending.family_black")
            && !execution.GolfBattleActive.HasValue
            && !execution.GolfBatteries.HasValue
            && !execution.GolfCutscenePlaying.HasValue,
            "Office_3 same-coroutine producer corroborator");

        FakeMemory office4 = new FakeMemory();
        office4.Int(Scene, 61);
        office4.Bool(Scene + EndingCutsceneOffset, true);
        office4.Int(Scene + EndingDeadStateOffset, 0);
        PutDictionary(office4, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(office4, ChoicesEntries, 0, 1, Keys, "OfficeEnd", -1);
        PutEntryValue(office4, ChoicesEntries, 0, 1);
        ReadResult family = EndingReader(office4).Read("Office_4");
        Check(family.Valid && family.EndingSignals.Contains("ending.family_black"),
            "Office_4 same-coroutine producer corroborator");

        PutEntryValue(office4, ChoicesEntries, 0, 2);
        ReadResult wrongOfficeValue = EndingReader(office4).Read("Office_4");
        Check(wrongOfficeValue.Valid && wrongOfficeValue.EndingSignals.Count == 0,
            "OfficeEnd value is required, not key presence");

        FakeMemory golf = new FakeMemory();
        golf.Int(Scene, 62);
        golf.Bool(Scene + EndingCutsceneOffset, false);
        golf.Int(Scene + EndingDeadStateOffset, 0);
        PutDictionary(golf, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(golf, ChoicesEntries, 0, 1, Keys, "Batteries", -1);
        PutEntryValue(golf, ChoicesEntries, 0, 2);
        ReadResult active = EndingReader(golf).Read("T_Boss");
        Check(active.Valid && active.GolfBattleActive == true
            && active.GolfBatteries == 2
            && active.GolfCutscenePlaying == false
            && active.EndingSignals.Count == 0,
            "T_Boss positive battle observation");

        PutEntryValue(golf, ChoicesEntries, 0, 0);
        ReadResult activeZero = EndingReader(golf).Read("T_Boss");
        Check(activeZero.Valid && activeZero.GolfBattleActive == true
            && activeZero.GolfBatteries == 0
            && activeZero.GolfCutscenePlaying == false
            && activeZero.EndingSignals.Count == 0,
            "T_Boss zero-battery active window remains source-confirmed");

        PutEntryValue(golf, ChoicesEntries, 0, -2);
        ReadResult activeNegative = EndingReader(golf).Read("T_Boss");
        Check(activeNegative.Valid && activeNegative.GolfBattleActive == true
            && activeNegative.GolfBatteries == -2
            && activeNegative.GolfCutscenePlaying == false
            && activeNegative.EndingSignals.Count == 0,
            "T_Boss negative-battery active window remains source-confirmed");

        golf.Bool(Scene + EndingCutsceneOffset, true);
        PutEntryValue(golf, ChoicesEntries, 0, 0);
        ReadResult loss = EndingReader(golf).Read("T_Boss");
        Check(loss.Valid && loss.GolfBattleActive == false
            && loss.GolfBatteries == 0
            && loss.GolfCutscenePlaying == true
            && loss.EndingSignals.Contains("ending.theodore_death"),
            "T_Boss loss corroborator and death marker");

        golf.Int(Scene, 300);
        ReadResult replayLoss = EndingReader(golf).Read("T_Boss");
        Check(replayLoss.Valid && replayLoss.GolfBattleActive == false
            && replayLoss.EndingSignals.Contains("ending.theodore_death"),
            "T_Boss saved victory marker must not veto current replay loss");
        PutEntryValue(golf, ChoicesEntries, 0, 2);
        ReadResult win = EndingReader(golf).Read("T_Boss");
        Check(win.Valid && win.GolfBattleActive == false
            && win.GolfBatteries == 2
            && win.GolfCutscenePlaying == true
            && !win.EndingSignals.Contains("ending.theodore_death"),
            "T_Boss win cutscene with remaining batteries is not death");

        FakeMemory finalDeath = new FakeMemory();
        finalDeath.Int(Scene, 63);
        finalDeath.Int(Scene + EndingDeadStateOffset, 3);
        long deadDictionary = 0x2600;
        long deadEntries = 0x2700;
        long deadKey = 0x2800;
        PutDictionary(finalDeath, PrisonStatic, deadDictionary, deadEntries, 1, 1, 1);
        PutEntry(finalDeath, deadEntries, 0, 1, deadKey, "Dead", -1);
        PutEntryValue(finalDeath, deadEntries, 0, 1);
        ReadResult finalResult = EndingReader(finalDeath).Read("Office_1");
        Check(finalResult.Valid && finalResult.EndingSignals.Contains("ending.final_death_black"),
            "final death producer corroborator");
        ReadResult cEnd = EndingReader(finalDeath).Read("C_End");
        Check(cEnd.Valid && !cEnd.EndingSignals.Contains("ending.final_death_black"),
            "C_End ordinary entry is not final death");
        Check(EndingReader(finalDeath).Read("B_End").EndingSignals.Count == 0
            && EndingReader(finalDeath).Read("B_Merdeka").EndingSignals.Count == 0,
            "B_End and B_Merdeka do not invent ending signals");

        FakeMemory cutsceneRace = new FakeMemory();
        cutsceneRace.Int(Scene, 64);
        cutsceneRace.Bool(Scene + EndingCutsceneOffset, true);
        cutsceneRace.Int(Scene + EndingDeadStateOffset, 0);
        PutDictionary(cutsceneRace, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(cutsceneRace, ChoicesEntries, 0, 1, Keys, "OfficeEnd", -1);
        PutEntryValue(cutsceneRace, ChoicesEntries, 0, 2);
        Action<FakeMemory, long> cutsceneMutation = null;
        cutsceneMutation = delegate(FakeMemory m, long address) {
            if (address == ChoicesEntries + 32 + 16)
                m.Bool(Scene + EndingCutsceneOffset, false);
            else
                m.AfterDialoguePayloadRead = cutsceneMutation;
        };
        cutsceneRace.AfterDialoguePayloadRead = cutsceneMutation;
        ReadResult cutsceneRaced = EndingReader(cutsceneRace).Read("Office_3");
        Check(cutsceneRaced.Valid && cutsceneRaced.EndingSignals.Count == 0
            && cutsceneRaced.Diagnostic.IndexOf("ending-cutscene-mutated", StringComparison.Ordinal) >= 0,
            "CutscenePlaying race fails closed");

        FakeMemory deadStateRace = new FakeMemory();
        deadStateRace.Int(Scene, 65);
        deadStateRace.Int(Scene + EndingDeadStateOffset, 3);
        long racedDeadDictionary = 0x2900;
        long racedDeadEntries = 0x2A00;
        long racedDeadKey = 0x2B00;
        PutDictionary(deadStateRace, PrisonStatic, racedDeadDictionary, racedDeadEntries, 1, 1, 1);
        PutEntry(deadStateRace, racedDeadEntries, 0, 1, racedDeadKey, "Dead", -1);
        PutEntryValue(deadStateRace, racedDeadEntries, 0, 1);
        Action<FakeMemory, long> deadStateMutation = null;
        deadStateMutation = delegate(FakeMemory m, long address) {
            if (address == racedDeadEntries + 32)
                m.Int(Scene + EndingDeadStateOffset, 0);
            else
                m.AfterDialoguePayloadRead = deadStateMutation;
        };
        deadStateRace.AfterDialoguePayloadRead = deadStateMutation;
        ReadResult deadStateRaced = EndingReader(deadStateRace).Read("Office_1");
        Check(deadStateRaced.Valid && deadStateRaced.EndingSignals.Count == 0
            && deadStateRaced.Diagnostic.IndexOf("ending-deadstate-mutated", StringComparison.Ordinal) >= 0,
            "DeadState race fails closed");

        FakeMemory valueRace = new FakeMemory();
        valueRace.Int(Scene, 66);
        valueRace.Bool(Scene + EndingCutsceneOffset, true);
        valueRace.Int(Scene + EndingDeadStateOffset, 0);
        PutDictionary(valueRace, ChoicesStatic, ChoicesObject, ChoicesEntries, 1, 1, 1);
        PutEntry(valueRace, ChoicesEntries, 0, 1, Keys, "OfficeEnd", -1);
        PutEntryValue(valueRace, ChoicesEntries, 0, 2);
        Action<FakeMemory, long> valueMutation = null;
        valueMutation = delegate(FakeMemory m, long address) {
            if (address == ChoicesEntries + 32 + 16)
                m.Int(ChoicesEntries + 32 + 16, 1);
            else
                m.AfterDialoguePayloadRead = valueMutation;
        };
        valueRace.AfterDialoguePayloadRead = valueMutation;
        ReadResult raced = EndingReader(valueRace).Read("Office_3");
        Check(raced.Valid && raced.EndingSignals.Count == 0
            && raced.Diagnostic.IndexOf("ending-office-end-mutated", StringComparison.Ordinal) >= 0,
            "OfficeEnd value race fails closed");
    }

    private static void TestMerdekaOutcomeReader()
    {
        FakeMemory memory = new FakeMemory();
        memory.Int(Scene, 49);
        ReadOnlyReader reader = Reader(memory);
        ReadResult fresh = reader.Read("B_Merdeka");
        Check(fresh.Valid && fresh.MerdekaStage == 49
            && fresh.EndingSignals.Count == 0,
            "stable B_Merdeka stage 49 arms no terminal event");

        memory.Int(Scene, 52);
        ReadResult victory = reader.Read("B_Merdeka");
        Check(victory.Valid && victory.MerdekaStage == 52
            && victory.EndingSignals.Contains("ending.merdeka_victory")
            && !victory.EndingSignals.Contains("ending.merdeka_defeat"),
            "stable armed stage 52 emits Merdeka victory");
        memory.Int(Scene, 51);
        Check(reader.Read("B_Merdeka").EndingSignals.Count == 0,
            "terminal outcome is consumed and cannot change to loss");

        memory.Int(Scene, 49);
        reader.Read("B_Merdeka");
        memory.Int(Scene, 51);
        ReadResult defeat = reader.Read("B_Merdeka");
        Check(defeat.Valid && defeat.MerdekaStage == 51
            && defeat.EndingSignals.Contains("ending.merdeka_defeat")
            && !defeat.EndingSignals.Contains("ending.merdeka_victory"),
            "fresh stage 49 arm followed by stage 51 emits Merdeka defeat");

        FakeMemory firstTerminalMemory = new FakeMemory();
        firstTerminalMemory.Int(Scene, 52);
        ReadResult firstTerminal = Reader(firstTerminalMemory).Read("B_Merdeka");
        Check(firstTerminal.Valid && firstTerminal.MerdekaStage == 52
            && firstTerminal.EndingSignals.Count == 0,
            "first observed terminal stage cannot emit a retroactive win");

        FakeMemory invalid = new FakeMemory();
        invalid.Int(Scene, 49);
        ReadOnlyReader invalidReader = Reader(invalid);
        invalidReader.Read("B_Merdeka");
        invalid.FailInt(Scene);
        Check(!invalidReader.Read("B_Merdeka").Valid,
            "invalid Abyss gap does not commit a Merdeka arm");
        invalid.RecoverInt(Scene);
        invalid.Int(Scene, 51);
        Check(invalidReader.Read("B_Merdeka").EndingSignals.Contains("ending.merdeka_defeat"),
            "same-scene invalid gap retains a prior Merdeka arm");

        FakeMemory unstable = new FakeMemory();
        unstable.Int(Scene, 49);
        unstable.AfterAbyssRead = delegate(FakeMemory m) { m.Int(Scene, 52); };
        ReadOnlyReader unstableReader = Reader(unstable);
        Check(!unstableReader.Read("B_Merdeka").Valid,
            "mutated stage 49 sample cannot commit a Merdeka arm");
        unstable.Int(Scene, 52);
        Check(unstableReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "mutated stage 49 sample cannot retro-finish a win");

        FakeMemory rejected = new FakeMemory();
        rejected.Int(Scene, 49);
        ReadOnlyReader rejectedReader = Reader(rejected);
        rejectedReader.Read("B_Merdeka");
        rejected.Int(Scene, 50);
        ReadResult foreign = rejectedReader.Read("B_Merdeka");
        Check(foreign.Valid && foreign.MerdekaStage == 50
            && foreign.EndingSignals.Count == 0,
            "stage 50 is not a Merdeka outcome");
        rejected.Int(Scene, 52);
        Check(rejectedReader.Read("B_Merdeka").EndingSignals.Contains("ending.merdeka_victory"),
            "stage 50 does not replace a qualified stage-49 arm");

        FakeMemory reset = new FakeMemory();
        reset.Int(Scene, 49);
        ReadOnlyReader resetReader = Reader(reset);
        resetReader.Read("B_Merdeka");
        resetReader.ResetMerdekaBinding();
        reset.Int(Scene, 52);
        Check(resetReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "ResetMerdekaBinding clears a pending outcome");

        FakeMemory scene = new FakeMemory();
        scene.Int(Scene, 49);
        ReadOnlyReader sceneReader = Reader(scene);
        sceneReader.Read("B_Merdeka");
        scene.Int(Scene, 52);
        Check(sceneReader.Read("B_10").EndingSignals.Count == 0,
            "Merdeka outcome is not emitted outside B_Merdeka");
        Check(sceneReader.Read("B_Merdeka").EndingSignals.Count == 0,
            "scene exit clears the Merdeka arm");
    }

    private static void TestTheodoreVictoryReader()
    {
        FakeMemory memory = new FakeMemory();
        PutTheodore(memory, 100, false);
        ReadOnlyReader reader = TheodoreReader(memory);
        ReadResult active = reader.Read("T_Boss");
        Check(active.Valid && active.BoundaryMetadataReady && active.TheodoreMetadataReady
            && active.TheodoreHp == 100
            && !active.EndingSignals.Contains("ending.theodore_victory"),
            "positive stable T_Boss HP binds qualified sole slider");

        PutTheodore(memory, 5, true);
        ReadResult intermediateCutscene = reader.Read("T_Boss");
        Check(intermediateCutscene.Valid && intermediateCutscene.TheodoreHp == 5
            && !intermediateCutscene.EndingSignals.Contains("ending.theodore_victory"),
            "positive-HP cutscene cannot finish Ending9");

        PutTheodore(memory, 0, true);
        ReadResult win = reader.Read("T_Boss");
        Check(win.Valid && win.TheodoreHp == 0
            && win.EndingSignals.Contains("ending.theodore_victory"),
            "stable HP-zero victory cutscene emits reader-owned Ending9");

        FakeMemory zeroFirstMemory = new FakeMemory();
        PutTheodore(zeroFirstMemory, 0, true);
        ReadResult zeroFirst = TheodoreReader(zeroFirstMemory).Read("T_Boss");
        Check(zeroFirst.Valid && !zeroFirst.TheodoreHp.HasValue
            && !zeroFirst.EndingSignals.Contains("ending.theodore_victory"),
            "HP-zero first observation cannot bind a finished boss");

        FakeMemory duplicateMemory = new FakeMemory();
        PutTheodore(duplicateMemory, 100, false, true, true, 2);
        ReadResult duplicate = TheodoreReader(duplicateMemory).Read("T_Boss");
        Check(duplicate.Valid && !duplicate.TheodoreHp.HasValue
            && !duplicate.EndingSignals.Contains("ending.theodore_victory"),
            "duplicate exact sliders fail closed");

        FakeMemory wrongClassMemory = new FakeMemory();
        PutTheodore(wrongClassMemory, 100, false, false);
        ReadResult wrongClass = TheodoreReader(wrongClassMemory).Read("T_Boss");
        Check(wrongClass.Valid && !wrongClass.TheodoreHp.HasValue,
            "wrong slider class cannot bind HP");

        FakeMemory zeroNativeMemory = new FakeMemory();
        PutTheodore(zeroNativeMemory, 100, false, true, false);
        ReadResult zeroNative = TheodoreReader(zeroNativeMemory).Read("T_Boss");
        Check(zeroNative.Valid && !zeroNative.TheodoreHp.HasValue,
            "zero native pointer cannot bind HP");

        FakeMemory raceMemory = new FakeMemory();
        PutTheodore(raceMemory, 5, false);
        ReadOnlyReader raceReader = TheodoreReader(raceMemory);
        Check(raceReader.Read("T_Boss").TheodoreHp == 5,
            "race fixture captures positive HP before victory");
        PutTheodore(raceMemory, 0, false);
        Check(!raceReader.Read("T_Boss").EndingSignals.Contains("ending.theodore_victory"),
            "HP drop before cutscene does not finish");
        PutTheodore(raceMemory, 0, true);
        Check(raceReader.Read("T_Boss").EndingSignals.Contains("ending.theodore_victory"),
            "next stable HP-zero cutscene sample finishes");

        Check(raceReader.Read("B_5").Valid,
            "leaving T_Boss remains a valid core reader sample");
        PutTheodore(raceMemory, 0, true);
        Check(!raceReader.Read("T_Boss").EndingSignals.Contains("ending.theodore_victory"),
            "scene exit clears retained Theodore binding");
    }

    public static int Main()
    {
        try
        {
            TestEmptyAndDeletedHoles();
            TestAbsentAndNullAreDifferent();
            TestPrisonAndSceneGating();
            TestBoundsAndMutation();
            TestReadFailuresAndAbyssRecheck();
            TestCheckpointSignal();
            TestMalformedOffsets();
            TestQualifiedDictionaryShape();
            TestDialogueSignal();
            TestManagedBoundaryReaders();
            TestTvStartReader();
            TestSurrenderChoiceReader();
            TestKarminaFinalChoiceReader();
            TestManagedStartReader();
            TestSceneBoundaryMetadataReadiness();
            TestSourceConfirmedEndingMarkers();
            TestMerdekaOutcomeReader();
            TestTheodoreVictoryReader();
            Console.WriteLine("PASS Peppered.Reader tests={0}", passed);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL Peppered.Reader: " + ex.Message);
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }
}
