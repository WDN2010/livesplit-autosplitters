using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Peppered
{
    public class ReadResult
    {
        public bool Valid;
        public int Abyss;
        public int? MerdekaStage;
        public bool? Bigman;
        public bool? Prison;
        public bool? HasCheckpoint;
        public bool? DialoguePlaying;
        public bool? TvStartActive;
        public bool? StartReady;
        public bool? GolfBattleActive;
        public int? GolfBatteries;
        public bool? GolfCutscenePlaying;
        public int? TheodoreHp;
        public HashSet<string> EndingSignals = new HashSet<string>();
        public bool BoundaryMetadataReady;
        public bool TheodoreMetadataReady;
        public bool KarminaMetadataReady;
        public int? KarminaChoice;
        public string KarminaPhase;
        public bool SurrenderMetadataReady;
        public int? SurrenderChoice;
        public string SurrenderPhase;
        public bool TvStartMetadataReady;
        public string Diagnostic;
    }

    // This reader only observes already-resolved managed fields.  It never writes
    // game memory and deliberately does not retain a memory watcher between reads.
    public class ReadOnlyReader
    {
        private const int PointerSize = 8;
        private const int StringHeaderLengthOffset = 16;
        private const int ArrayLengthOffset = 24;
        private const int ArrayDataOffset = 32;
        private const int MaxDictionaryCount = 4096;
        private const int MaxDictionaryCapacity = 8192;
        private const int MaxKeyLength = 64;
        private const int MaxDialogueLineCount = 4096;
        private const int MaxStaticFieldOffset = 0x100;
        private const int MaxInstanceFieldOffset = 0x400;
        // Pinned Windows Mono 6.13 x64 Unity.TextMeshPro.dll field offset.
        // This is an allowlist for TMP_Text.m_maxVisibleCharacters only;
        // unrelated managed fields retain the bounded generic validator.
        private const int QualifiedTmpVisibleCharactersOffset = 1268;
        private const int MaxTheodoreSelectableCount = 256;
        private const string MerdekaSurrenderSignal = "ending.merdeka_surrender";
        private const string KarminaFinalChoiceSignal = "ending.karmina_final_choice";
        private const int TheodoreNavigationModeOffset = 0;
        // These are object offsets; Entry metadata offsets are already normalized
        // by the asl-help MonoField.Offset getter for Entry[] scanning.
        private const int DictionaryBucketsOffset = 16;
        private const int DictionaryEntriesOffset = 24;
        private const int DictionaryComparerOffset = 32;
        private const int DictionaryKeysOffset = 40;
        private const int DictionaryValuesOffset = 48;
        private const int DictionarySyncRootOffset = 56;
        private const int DictionaryCountOffset = 64;
        private const int DictionaryFreeListOffset = 68;
        private const int DictionaryFreeCountOffset = 72;
        private const int DictionaryVersionOffset = 76;
        private const int EntryHashCodeOffset = 0;
        private const int EntryNextOffset = 4;
        private const int EntryKeyOffset = 8;
        private const int EntryValueOffset = 16;
        private const int QualifiedEntryStride = 24;
        private const int GenericClassCachedClassOffset = 32;

        internal interface IReaderMemory
        {
            bool TryReadInt(IntPtr address, out int value);
            bool TryReadBool(IntPtr address, out bool value);
            bool TryReadPointer(IntPtr address, out IntPtr value);
            bool TryReadString(IntPtr address, out string value);
        }

        private sealed class DynamicHelperMemory : IReaderMemory
        {
            private readonly object helper;

            internal DynamicHelperMemory(dynamic helper)
            {
                this.helper = helper;
            }

            public bool TryReadInt(IntPtr address, out int value)
            {
                // Keep this as a fresh helper.TryRead<int> on every sample.  Do not
                // replace it with a cached MemoryWatcher or a prior value.
                dynamic activeHelper = helper;
                return activeHelper.TryRead<int>(out value, address);
            }

            public bool TryReadBool(IntPtr address, out bool value)
            {
                dynamic activeHelper = helper;
                return activeHelper.TryRead<bool>(out value, address);
            }

            public bool TryReadPointer(IntPtr address, out IntPtr value)
            {
                dynamic activeHelper = helper;
                long raw;
                bool ok = activeHelper.TryRead<long>(out raw, address);
                value = ok ? new IntPtr(raw) : IntPtr.Zero;
                return ok;
            }

            public bool TryReadString(IntPtr address, out string value)
            {
                // Bound the allocation in this actual read, not only in an earlier
                // check: a remote pointer/length can change between two reads.
                dynamic activeHelper = helper;
                value = null;
                int length;
                if (!activeHelper.TryRead<int>(out length, Address(address, StringHeaderLengthOffset))
                    || length < 0 || length > 64) return false;
                byte[] bytes;
                if (!activeHelper.TryReadSpan<byte>(out bytes, checked(length * 2),
                    Address(address, StringHeaderLengthOffset + 4))
                    || bytes == null || bytes.Length != length * 2) return false;
                value = new System.Text.UnicodeEncoding(false, false, true).GetString(bytes);
                return true;
            }
        }

        internal sealed class StaticField
        {
            internal IntPtr Storage;
            internal int Offset;
        }

        // The shape is produced from closed Mono field/type metadata.  It is
        // private to the reader; tests use the internal factory below and never
        // mock Mono.
        internal sealed class DictionaryLayout
        {
            internal StaticField StaticField;
            internal int EntriesOffset;
            internal int CountOffset;
            internal int VersionOffset;
            internal int HashCodeOffset;
            internal int NextOffset;
            internal int KeyOffset;
            internal int ValueOffset;
            internal int EntryStride;
        }

        internal sealed class DialogueLayout
        {
            internal StaticField InstanceField;
            internal int PlayingOffset;
            internal int LinesOffset;
            internal int CurrentLineOffset;
            internal int TextBoxOffset;
            internal int VisibleCharactersOffset;
        }

        // The TV-start endpoint intentionally has no TMP, chooser, stars, or
        // rendered-text dependency.  It observes only the managed producer's
        // DialogueManager instance and its m36 payload.
        internal sealed class TvStartLayout
        {
            internal StaticField InstanceStaticAddress;
            internal IntPtr DialogueClassAddress;
            internal int LinesOffset;
            internal int CurrentLineOffset;
            internal int PlayingOffset;
            internal bool TestDirectClassIdentity;
        }

        internal sealed class StartLayout
        {
            internal StaticField CutscenePlayingField;
            internal StaticField InstanceField;
            internal int PlayerOffset;
            internal int CanMoveOffset;
        }

        internal sealed class SurrenderLayout
        {
            internal StaticField InstanceField;
            internal IntPtr DialogueClassAddress;
            internal IntPtr ButtonChooseClassAddress;
            internal int DialogueLinesOffset;
            internal int CurrentLineOffset;
            internal int LeftStringOffset;
            internal int RightStringOffset;
            internal int PlayingOffset;
            internal int TextingOffset;
            internal int ButtonChooseOffset;
            internal int ButtonChoiceOffset;
            internal bool TestDirectClassIdentity;
        }

        internal sealed class EndingLayout
        {
            internal StaticField CutscenePlayingField;
            internal StaticField DeadStateField;
        }

        // Qualified metadata for the Unity UI registry and Slider fields. The
        // retained identity below is deliberately not a MemoryWatcher.
        internal sealed class TheodoreLayout
        {
            internal StaticField SelectablesField;
            internal StaticField SelectableCountField;
            internal IntPtr ImageAddress;
            internal IntPtr SelectableClassAddress;
            internal IntPtr SliderClassAddress;
            internal IntPtr NavigationClassAddress;
            internal IntPtr ObjectClassAddress;
            internal IntPtr RectTransformClassAddress;
            internal IntPtr GraphicClassAddress;
            internal int NavigationOffset;
            internal int NavigationModeOffset;
            internal int TargetGraphicOffset;
            internal int CurrentIndexOffset;
            internal int CachedPtrOffset;
            internal int FillRectOffset;
            internal int HandleRectOffset;
            internal int DirectionOffset;
            internal int MinValueOffset;
            internal int MaxValueOffset;
            internal int WholeNumbersOffset;
            internal int ValueOffset;
            internal bool TestDirectClassIdentity;
            internal Func<IntPtr, IntPtr> ClassParent;
            internal readonly HashSet<IntPtr> ProvenSelectableClasses = new HashSet<IntPtr>();

            internal TheodoreLayout()
            {
            }
        }

        private sealed class DictionaryRead
        {
            internal bool Available;
            internal bool Found;
            internal bool ValueFound;
            internal int Value;
            internal IntPtr ValueEntry;
        }

        private sealed class EndingObservation
        {
            internal bool? GolfBattleActive;
            internal int? GolfBatteries;
            internal bool? GolfCutscenePlaying;
            internal HashSet<string> Signals = new HashSet<string>();
            internal TheodorePoll Theodore;
            internal KarminaPoll Karmina;
            internal SurrenderPoll Surrender;
        }

        private sealed class TheodoreBinding
        {
            internal IntPtr Object;
            internal IntPtr Native;
            internal IntPtr Class;
            internal IntPtr Image;
            internal IntPtr FillRect;
        }

        private sealed class TheodorePoll
        {
            internal bool Coherent;
            internal int Hp;
            internal bool Terminal;
            internal bool ClearCache;
            internal TheodoreBinding Capture;
        }

        private sealed class TheodoreRegistry
        {
            internal IntPtr Candidate;
            internal int CandidateIndex;
            internal int ExactSliderCount;
        }

        private sealed class SurrenderBinding
        {
            internal IntPtr Dialogue;
            internal IntPtr DialogueClass;
            internal IntPtr ButtonChoose;
            internal IntPtr ButtonChooseClass;
            internal IntPtr Lines;
            internal IntPtr FirstLine;
            internal IntPtr LeftString;
            internal IntPtr RightString;
        }

        private sealed class SurrenderPoll
        {
            internal bool Coherent;
            internal bool Context;
            internal int Choice;
            internal SurrenderBinding Capture;
            internal bool Confirmed;
            internal bool ClearBinding;
        }

        private sealed class KarminaPoll
        {
            internal bool Coherent;
            internal bool Context;
            internal int Choice;
            internal SurrenderBinding Capture;
            internal bool Confirmed;
            internal bool ClearBinding;
        }

        private sealed class SurrenderFrame
        {
            internal IntPtr Dialogue;
            internal IntPtr DialogueClass;
            internal IntPtr ButtonChoose;
            internal IntPtr ButtonChooseClass;
            internal IntPtr Lines;
            internal IntPtr FirstLine;
            internal string Line;
            internal int LineCount;
            internal int CurrentLine;
            internal IntPtr LeftString;
            internal string Left;
            internal IntPtr RightString;
            internal string Right;
            internal bool Playing;
            internal bool Texting;
            internal int Choice;
        }

        private sealed class TvStartFrame
        {
            internal IntPtr Dialogue;
            internal IntPtr DialogueClass;
            internal IntPtr Lines;
            internal int LineCount;
            internal IntPtr FirstLine;
            internal string Line;
            internal int CurrentLine;
            internal bool Playing;
        }

        private sealed class TheodoreFingerprint
        {
            internal IntPtr Class;
            internal IntPtr Native;
            internal IntPtr FillRect;
            internal IntPtr HandleRect;
            internal IntPtr TargetGraphic;
            internal int NavigationMode;
            internal int Direction;
            internal int CurrentIndex;
            internal bool HasCurrentIndex;
            internal float Min;
            internal float Max;
            internal bool WholeNumbers;
        }

        private IReaderMemory memory;
        private StaticField abyss;
        private StaticField checkpoint;
        private DictionaryLayout choices;
        private DictionaryLayout collectibles;
        private DialogueLayout dialogue;
        private TvStartLayout tvStart;
        private StartLayout start;
        private EndingLayout ending;
        private TheodoreLayout theodore;
        private TheodoreBinding theodoreBinding;
        private SurrenderLayout surrender;
        private SurrenderBinding surrenderBinding;
        private string surrenderScene;
        private SurrenderBinding karminaBinding;
        private string karminaScene;
        private bool configured;
        private string configurationDiagnostic;
        private bool merdekaOutcomeArmed;

        public ReadOnlyReader()
        {
        }

        // Test-only construction point. It exercises the same bounded reader and
        // scanner as Configure without fabricating a dynamic Mono object graph.
        internal ReadOnlyReader(IReaderMemory memory, IntPtr sceneStorage, int abyssOffset,
            DictionaryLayout choices, DictionaryLayout collectibles)
            : this(memory, sceneStorage, abyssOffset, choices, collectibles, -1, null)
        {
        }

        internal ReadOnlyReader(IReaderMemory memory, IntPtr sceneStorage, int abyssOffset,
            DictionaryLayout choices, DictionaryLayout collectibles, int checkpointOffset)
            : this(memory, sceneStorage, abyssOffset, choices, collectibles, checkpointOffset, null)
        {
        }

        internal ReadOnlyReader(IReaderMemory memory, IntPtr sceneStorage, int abyssOffset,
            DictionaryLayout choices, DictionaryLayout collectibles, int checkpointOffset,
            DialogueLayout dialogue)
            : this(memory, sceneStorage, abyssOffset, choices, collectibles, checkpointOffset,
                dialogue, null)
        {
        }

        internal ReadOnlyReader(IReaderMemory memory, IntPtr sceneStorage, int abyssOffset,
            DictionaryLayout choices, DictionaryLayout collectibles, int checkpointOffset,
            DialogueLayout dialogue, StartLayout start)
            : this(memory, sceneStorage, abyssOffset, choices, collectibles, checkpointOffset,
                dialogue, start, null)
        {
        }

        internal ReadOnlyReader(IReaderMemory memory, IntPtr sceneStorage, int abyssOffset,
            DictionaryLayout choices, DictionaryLayout collectibles, int checkpointOffset,
            DialogueLayout dialogue, StartLayout start, EndingLayout ending)
            : this(memory, sceneStorage, abyssOffset, choices, collectibles, checkpointOffset,
                dialogue, start, ending, null)
        {
        }

        internal ReadOnlyReader(IReaderMemory memory, IntPtr sceneStorage, int abyssOffset,
            DictionaryLayout choices, DictionaryLayout collectibles, int checkpointOffset,
            DialogueLayout dialogue, StartLayout start, EndingLayout ending,
            TheodoreLayout theodore)
            : this(memory, sceneStorage, abyssOffset, choices, collectibles, checkpointOffset,
                dialogue, start, ending, theodore, null)
        {
        }

        internal ReadOnlyReader(IReaderMemory memory, IntPtr sceneStorage, int abyssOffset,
            DictionaryLayout choices, DictionaryLayout collectibles, int checkpointOffset,
            DialogueLayout dialogue, StartLayout start, EndingLayout ending,
            TheodoreLayout theodore, SurrenderLayout surrender)
            : this(memory, sceneStorage, abyssOffset, choices, collectibles, checkpointOffset,
                dialogue, start, ending, theodore, surrender, null)
        {
        }

        internal ReadOnlyReader(IReaderMemory memory, IntPtr sceneStorage, int abyssOffset,
            DictionaryLayout choices, DictionaryLayout collectibles, int checkpointOffset,
            DialogueLayout dialogue, StartLayout start, EndingLayout ending,
            TheodoreLayout theodore, SurrenderLayout surrender, TvStartLayout tvStart)
        {
            this.memory = memory;
            this.abyss = sceneStorage != IntPtr.Zero && IsValidStaticOffset(abyssOffset, 4)
                ? new StaticField { Storage = sceneStorage, Offset = abyssOffset } : null;
            this.checkpoint = checkpointOffset < 0 ? null
                : (sceneStorage != IntPtr.Zero && IsValidStaticOffset(checkpointOffset, PointerSize)
                    ? new StaticField { Storage = sceneStorage, Offset = checkpointOffset } : null);
            this.choices = choices;
            this.collectibles = collectibles;
            this.dialogue = IsValidDialogueLayout(dialogue) ? dialogue : null;
            this.tvStart = IsValidTvStartLayout(tvStart) ? tvStart : null;
            this.start = IsValidStartLayout(start) ? start : null;
            this.ending = IsValidEndingLayout(ending) ? ending : null;
            this.theodore = IsValidTheodoreLayout(theodore) ? theodore : null;
            this.surrender = IsValidSurrenderLayout(surrender) ? surrender : null;
            this.theodoreBinding = null;
            this.surrenderBinding = null;
            this.surrenderScene = null;
            this.karminaBinding = null;
            this.karminaScene = null;
            this.configured = IntPtr.Size == PointerSize && this.abyss != null;
            this.configurationDiagnostic = IntPtr.Size != PointerSize
                ? "unsupported-pointer-size" : (this.abyss == null ? "metadata-unavailable" : null);
            if (checkpointOffset >= 0 && this.checkpoint == null)
                this.configurationDiagnostic = Append(this.configurationDiagnostic,
                    "checkpoint-metadata-unavailable");
            if (theodore != null && this.theodore == null)
                this.configurationDiagnostic = Append(this.configurationDiagnostic,
                    "theodore-metadata-unavailable");
            if (surrender != null && this.surrender == null)
                this.configurationDiagnostic = Append(this.configurationDiagnostic,
                    "surrender-metadata-unavailable");
            if (tvStart != null && this.tvStart == null)
                this.configurationDiagnostic = Append(this.configurationDiagnostic,
                    "tv-start-metadata-unavailable");
        }

        internal static ReadOnlyReader ForTests(IReaderMemory memory, IntPtr sceneStorage,
            int abyssOffset, DictionaryLayout choices, DictionaryLayout collectibles)
        {
            return new ReadOnlyReader(memory, sceneStorage, abyssOffset, choices, collectibles);
        }

        internal static ReadOnlyReader ForTests(IReaderMemory memory, IntPtr sceneStorage,
            int abyssOffset, DictionaryLayout choices, DictionaryLayout collectibles,
            int checkpointOffset)
        {
            return new ReadOnlyReader(memory, sceneStorage, abyssOffset, choices, collectibles,
                checkpointOffset);
        }

        internal static ReadOnlyReader ForTests(IReaderMemory memory, IntPtr sceneStorage,
            int abyssOffset, DictionaryLayout choices, DictionaryLayout collectibles,
            int checkpointOffset, DialogueLayout dialogue)
        {
            return new ReadOnlyReader(memory, sceneStorage, abyssOffset, choices, collectibles,
                checkpointOffset, dialogue);
        }

        internal static ReadOnlyReader ForTests(IReaderMemory memory, IntPtr sceneStorage,
            int abyssOffset, DictionaryLayout choices, DictionaryLayout collectibles,
            int checkpointOffset, DialogueLayout dialogue, StartLayout start)
        {
            return new ReadOnlyReader(memory, sceneStorage, abyssOffset, choices, collectibles,
                checkpointOffset, dialogue, start);
        }

        internal static ReadOnlyReader ForTests(IReaderMemory memory, IntPtr sceneStorage,
            int abyssOffset, DictionaryLayout choices, DictionaryLayout collectibles,
            int checkpointOffset, DialogueLayout dialogue, StartLayout start,
            EndingLayout ending)
        {
            return new ReadOnlyReader(memory, sceneStorage, abyssOffset, choices, collectibles,
                checkpointOffset, dialogue, start, ending);
        }

        internal static ReadOnlyReader ForTests(IReaderMemory memory, IntPtr sceneStorage,
            int abyssOffset, DictionaryLayout choices, DictionaryLayout collectibles,
            int checkpointOffset, DialogueLayout dialogue, StartLayout start,
            EndingLayout ending, TheodoreLayout theodore)
        {
            return new ReadOnlyReader(memory, sceneStorage, abyssOffset, choices, collectibles,
                checkpointOffset, dialogue, start, ending, theodore);
        }

        internal static ReadOnlyReader ForTests(IReaderMemory memory, IntPtr sceneStorage,
            int abyssOffset, DictionaryLayout choices, DictionaryLayout collectibles,
            int checkpointOffset, DialogueLayout dialogue, StartLayout start,
            EndingLayout ending, TheodoreLayout theodore, SurrenderLayout surrender)
        {
            return new ReadOnlyReader(memory, sceneStorage, abyssOffset, choices, collectibles,
                checkpointOffset, dialogue, start, ending, theodore, surrender);
        }

        internal static ReadOnlyReader ForTests(IReaderMemory memory, IntPtr sceneStorage,
            int abyssOffset, DictionaryLayout choices, DictionaryLayout collectibles,
            int checkpointOffset, DialogueLayout dialogue, StartLayout start,
            EndingLayout ending, TheodoreLayout theodore, SurrenderLayout surrender,
            TvStartLayout tvStart)
        {
            return new ReadOnlyReader(memory, sceneStorage, abyssOffset, choices, collectibles,
                checkpointOffset, dialogue, start, ending, theodore, surrender, tvStart);
        }

        // A compact fixture layout for reader tests. Production layouts are always
        // made by ResolveTheodore from Mono metadata; these identities are only
        // opaque handles for a fake IReaderMemory.
        internal static TheodoreLayout TestTheodoreLayout(IntPtr registryStorage,
            int selectablesOffset, int countOffset)
        {
            return TestTheodoreLayout(registryStorage, selectablesOffset, countOffset,
                new IntPtr(0x1001), new IntPtr(0x1002), new IntPtr(0x1003),
                new IntPtr(0x1004), new IntPtr(0x1005), new IntPtr(0x1006),
                new IntPtr(0x1007));
        }

        internal static TheodoreLayout TestTheodoreLayout(IntPtr registryStorage,
            int selectablesOffset, int countOffset, IntPtr imageAddress,
            IntPtr selectableClassAddress, IntPtr sliderClassAddress,
            IntPtr navigationClassAddress, IntPtr objectClassAddress,
            IntPtr rectTransformClassAddress, IntPtr graphicClassAddress)
        {
            return new TheodoreLayout {
                SelectablesField = new StaticField {
                    Storage = registryStorage, Offset = selectablesOffset
                },
                SelectableCountField = new StaticField {
                    Storage = registryStorage, Offset = countOffset
                },
                ImageAddress = imageAddress,
                SelectableClassAddress = selectableClassAddress,
                SliderClassAddress = sliderClassAddress,
                NavigationClassAddress = navigationClassAddress,
                ObjectClassAddress = objectClassAddress,
                RectTransformClassAddress = rectTransformClassAddress,
                GraphicClassAddress = graphicClassAddress,
                NavigationOffset = 24,
                NavigationModeOffset = TheodoreNavigationModeOffset,
                TargetGraphicOffset = 104,
                CurrentIndexOffset = 220,
                CachedPtrOffset = 16,
                FillRectOffset = 232,
                HandleRectOffset = 240,
                DirectionOffset = 296,
                MinValueOffset = 300,
                MaxValueOffset = 304,
                WholeNumbersOffset = 308,
                ValueOffset = 312,
                TestDirectClassIdentity = true
            };
        }

        internal static DictionaryLayout TestDictionaryLayout(IntPtr staticStorage, int staticOffset)
        {
            // ForTests takes the same helper-normalized Entry[] row offsets used
            // by production metadata.  The row begins at array data offset 32;
            // no Entry object-header adjustment belongs in this reader.
            return new DictionaryLayout {
                StaticField = new StaticField { Storage = staticStorage, Offset = staticOffset },
                EntriesOffset = 24,
                CountOffset = 64,
                VersionOffset = 76,
                HashCodeOffset = 0,
                NextOffset = 4,
                KeyOffset = 8,
                ValueOffset = 16,
                EntryStride = 24
            };
        }

        internal static DialogueLayout TestDialogueLayout(IntPtr staticStorage, int staticOffset,
            int playingOffset)
        {
            return new DialogueLayout {
                InstanceField = new StaticField { Storage = staticStorage, Offset = staticOffset },
                PlayingOffset = playingOffset,
                LinesOffset = -1,
                CurrentLineOffset = -1,
                TextBoxOffset = -1,
                VisibleCharactersOffset = -1
            };
        }

        internal static DialogueLayout TestDialogueLayout(IntPtr staticStorage, int staticOffset,
            int playingOffset, int linesOffset, int currentLineOffset, int textBoxOffset,
            int visibleCharactersOffset)
        {
            return new DialogueLayout {
                InstanceField = new StaticField { Storage = staticStorage, Offset = staticOffset },
                PlayingOffset = playingOffset,
                LinesOffset = linesOffset,
                CurrentLineOffset = currentLineOffset,
                TextBoxOffset = textBoxOffset,
                VisibleCharactersOffset = visibleCharactersOffset
            };
        }

        internal static TvStartLayout TestTvStartLayout(IntPtr staticStorage, int staticOffset,
            IntPtr dialogueClassAddress, int linesOffset, int currentLineOffset,
            int playingOffset)
        {
            return new TvStartLayout {
                InstanceStaticAddress = new StaticField {
                    Storage = staticStorage, Offset = staticOffset
                },
                DialogueClassAddress = dialogueClassAddress,
                LinesOffset = linesOffset,
                CurrentLineOffset = currentLineOffset,
                PlayingOffset = playingOffset,
                TestDirectClassIdentity = true
            };
        }

        internal static SurrenderLayout TestSurrenderLayout(IntPtr staticStorage,
            int staticOffset)
        {
            return TestSurrenderLayout(staticStorage, staticOffset,
                new IntPtr(0x11020), new IntPtr(0x11021));
        }

        internal static SurrenderLayout TestSurrenderLayout(IntPtr staticStorage,
            int staticOffset, IntPtr dialogueClassAddress,
            IntPtr buttonChooseClassAddress)
        {
            return new SurrenderLayout {
                InstanceField = new StaticField { Storage = staticStorage, Offset = staticOffset },
                DialogueClassAddress = dialogueClassAddress,
                ButtonChooseClassAddress = buttonChooseClassAddress,
                DialogueLinesOffset = 96,
                CurrentLineOffset = 188,
                LeftStringOffset = 80,
                RightStringOffset = 88,
                PlayingOffset = 196,
                TextingOffset = 200,
                ButtonChooseOffset = 40,
                ButtonChoiceOffset = 32,
                TestDirectClassIdentity = true
            };
        }

        internal static StartLayout TestStartLayout(IntPtr staticStorage,
            int cutscenePlayingOffset, int instanceOffset, int playerOffset, int canMoveOffset)
        {
            return new StartLayout {
                CutscenePlayingField = new StaticField {
                    Storage = staticStorage, Offset = cutscenePlayingOffset
                },
                InstanceField = new StaticField { Storage = staticStorage, Offset = instanceOffset },
                PlayerOffset = playerOffset,
                CanMoveOffset = canMoveOffset
            };
        }

        internal static EndingLayout TestEndingLayout(IntPtr staticStorage,
            int cutscenePlayingOffset, int deadStateOffset)
        {
            return new EndingLayout {
                CutscenePlayingField = new StaticField {
                    Storage = staticStorage, Offset = cutscenePlayingOffset
                },
                DeadStateField = new StaticField {
                    Storage = staticStorage, Offset = deadStateOffset
                }
            };
        }

        // Configure must receive the exact asl-help Mono manager and helper objects.
        // MonoField.Name is intentionally used (rather than raw metadata names), so
        // <Abyss_State>k__BackingField resolves as the normalized Abyss_State.
        public void Configure(dynamic helper, dynamic mono)
        {
            memory = null;
            abyss = null;
            checkpoint = null;
            choices = null;
            collectibles = null;
            dialogue = null;
            tvStart = null;
            start = null;
            ending = null;
            theodore = null;
            surrender = null;
            theodoreBinding = null;
            surrenderBinding = null;
            surrenderScene = null;
            karminaBinding = null;
            karminaScene = null;
            merdekaOutcomeArmed = false;
            configured = false;
            configurationDiagnostic = null;

            if (IntPtr.Size != PointerSize)
            {
                configurationDiagnostic = "unsupported-pointer-size";
                return;
            }
            if (helper == null || mono == null)
            {
                configurationDiagnostic = "metadata-unavailable";
                return;
            }

            memory = new DynamicHelperMemory(helper);
            object sceneClass;
            StaticField sceneStorage;
            try
            {
                sceneClass = mono["MySceneManager"];
                sceneStorage = ResolveStorage(sceneClass, "MySceneManager");
                object abyssField = FindField(sceneClass, "Abyss_State");
                if (abyssField == null || !IsInt32Type(GetMember(abyssField, "Type")))
                    throw new InvalidOperationException("abyss-field-unavailable");
                int abyssOffset = GetOffset(abyssField);
                if (!IsValidStaticOffset(abyssOffset, 4))
                    throw new InvalidOperationException("abyss-offset-unavailable");
                abyss = new StaticField {
                    Storage = sceneStorage.Storage,
                    Offset = abyssOffset
                };
                configured = true;
            }
            catch (Exception)
            {
                configured = false;
                AddConfigurationDiagnostic("metadata-unavailable");
                return;
            }

            // Checkpoint, start, dictionaries, and dialogue are independent
            // optional metadata.  A malformed dictionary must not strand a valid
            // start or dialogue boundary resolver.
            try
            {
                object checkpointField = FindField(sceneClass, "CheckPointLvlName");
                if (checkpointField == null || !IsStringType(GetMember(checkpointField, "Type")))
                    throw new InvalidOperationException("checkpoint-metadata-unavailable");
                int checkpointOffset = GetOffset(checkpointField);
                if (!IsValidStaticOffset(checkpointOffset, PointerSize))
                    throw new InvalidOperationException("checkpoint-offset-unavailable");
                checkpoint = new StaticField {
                    Storage = sceneStorage.Storage,
                    Offset = checkpointOffset
                };
            }
            catch (Exception)
            {
                AddConfigurationDiagnostic("checkpoint-metadata-unavailable");
            }

            try
            {
                start = ResolveStart(sceneClass);
                if (start == null)
                    AddConfigurationDiagnostic("start-metadata-unavailable");
            }
            catch (Exception)
            {
                start = null;
                AddConfigurationDiagnostic("start-metadata-unavailable");
            }

            try
            {
                ending = ResolveEnding(sceneClass);
                if (ending == null || !IsValidStaticField(ending.CutscenePlayingField, 1))
                    AddConfigurationDiagnostic("ending-cutscene-metadata-unavailable");
                if (ending == null || !IsValidStaticField(ending.DeadStateField, 4))
                    AddConfigurationDiagnostic("ending-deadstate-metadata-unavailable");
            }
            catch (Exception)
            {
                ending = null;
                AddConfigurationDiagnostic("ending-cutscene-metadata-unavailable");
                AddConfigurationDiagnostic("ending-deadstate-metadata-unavailable");
            }

            string stateStage = "state-class";
            try
            {
                object stateClass = mono["WHAT_HAVE_I_DONE"];
                stateStage = "state-storage";
                StaticField stateStorage = ResolveStorage(stateClass, "WHAT_HAVE_I_DONE");
                stateStage = "choices-field";
                object choicesField = FindField(stateClass, "Choices");
                choices = ResolveDictionary(stateStorage, choicesField,
                    stateClass, "choices");
                stateStage = "collectibles-field";
                object collectiblesField = FindField(stateClass, "Collectibles");
                collectibles = ResolveDictionary(stateStorage, collectiblesField,
                    stateClass, "collectibles");
                if (choices == null)
                    AddConfigurationDiagnostic("choices-metadata-unavailable");
                if (collectibles == null)
                    AddConfigurationDiagnostic("collectibles-metadata-unavailable");
            }
            catch (Exception)
            {
                choices = null;
                collectibles = null;
                // Keep this bounded to stage labels; never expose exception text,
                // object addresses, or arbitrary metadata contents.
                AddConfigurationDiagnostic("choices-metadata-stage-" + stateStage);
                AddConfigurationDiagnostic("collectibles-metadata-stage-" + stateStage);
                AddConfigurationDiagnostic("choices-metadata-unavailable");
                AddConfigurationDiagnostic("collectibles-metadata-unavailable");
            }

            // Resolve the shared manager class once.  The minimal TV endpoint is
            // deliberately attempted independently of the older full dialogue /
            // TMP glyph resolver, so a glyph metadata failure cannot strand TV.
            object dialogueClassForSurrender = null;
            try
            {
                dialogueClassForSurrender = mono["DialogueManager"];
            }
            catch (Exception)
            {
                AddConfigurationDiagnostic("dialogue-metadata-stage-class");
            }

            try
            {
                dialogue = ResolveDialogue(mono, dialogueClassForSurrender,
                    new Action<string>(AddConfigurationDiagnostic));
                if (dialogue == null)
                    AddConfigurationDiagnostic("dialogue-metadata-unavailable");
            }
            catch (Exception)
            {
                dialogue = null;
                AddConfigurationDiagnostic("dialogue-metadata-stage-resolve");
                AddConfigurationDiagnostic("dialogue-metadata-unavailable");
            }

            try
            {
                tvStart = TryResolveTvStart(mono, dialogueClassForSurrender,
                    new Action<string>(AddConfigurationDiagnostic));
                if (tvStart == null)
                    AddConfigurationDiagnostic("tv-start-metadata-unavailable");
            }
            catch (Exception)
            {
                tvStart = null;
                AddConfigurationDiagnostic("tv-start-metadata-stage-resolve");
                AddConfigurationDiagnostic("tv-start-metadata-unavailable");
            }

            try
            {
                object buttonChooseClass = null;
                try { buttonChooseClass = mono["Button_Choose"]; }
                catch (Exception) { buttonChooseClass = null; }
                surrender = ResolveSurrender(mono, dialogueClassForSurrender,
                    buttonChooseClass, new Action<string>(AddConfigurationDiagnostic));
                if (surrender == null)
                    AddConfigurationDiagnostic("surrender-metadata-unavailable");
            }
            catch (Exception)
            {
                surrender = null;
                AddConfigurationDiagnostic("surrender-metadata-stage-resolve");
                AddConfigurationDiagnostic("surrender-metadata-unavailable");
            }

            try
            {
                theodore = ResolveTheodore(mono,
                    new Action<string>(AddConfigurationDiagnostic));
                if (theodore == null)
                    AddConfigurationDiagnostic("theodore-metadata-unavailable");
            }
            catch (Exception)
            {
                theodore = null;
                AddConfigurationDiagnostic("theodore-metadata-stage-resolve");
                AddConfigurationDiagnostic("theodore-metadata-unavailable");
            }
        }

        // Clear only the new retained-HP observation at explicit run/session
        // boundaries. Transient invalid samples in one attempt do not erase a
        // previously qualified identity, which is revalidated on every use.
        public void ResetTheodoreBinding()
        {
            theodoreBinding = null;
        }

        // Clear only the Ending 8 local prompt binding. The outer ASL owns
        // scene/run identity and calls this at explicit boundaries.
        public void ResetSurrenderBinding()
        {
            surrenderBinding = null;
            surrenderScene = null;
        }

        // Clear only the Against-Karmina final-choice binding. The outer ASL
        // owns scene/run identity and calls this at explicit boundaries.
        public void ResetKarminaBinding()
        {
            karminaBinding = null;
            karminaScene = null;
        }

        // Clear only the source-qualified Merdeka 49 -> 51/52 binding. The
        // outer ASL owns run and scene identity and calls this at every boundary.
        public void ResetMerdekaBinding()
        {
            merdekaOutcomeArmed = false;
        }

        public ReadResult Read(string scene)
        {
            ReadResult result = new ReadResult {
                Valid = false,
                Abyss = 0,
                MerdekaStage = null,
                Bigman = null,
                Prison = null,
                HasCheckpoint = null,
                DialoguePlaying = null,
                TvStartActive = null,
                StartReady = null,
                GolfBattleActive = null,
                GolfBatteries = null,
                GolfCutscenePlaying = null,
                TheodoreHp = null,
                EndingSignals = new HashSet<string>(),
                BoundaryMetadataReady = false,
                TheodoreMetadataReady = IsValidTheodoreLayout(theodore),
                KarminaMetadataReady = IsValidSurrenderLayout(surrender),
                KarminaChoice = null,
                KarminaPhase = "other",
                SurrenderMetadataReady = IsValidSurrenderLayout(surrender),
                SurrenderChoice = null,
                SurrenderPhase = "other",
                TvStartMetadataReady = false,
                Diagnostic = configurationDiagnostic
            };
            SetReadResultTvStartMetadataReady(result, IsValidTvStartLayout(tvStart));

            if (scene != "T_Boss")
                theodoreBinding = null;
            if (scene != "B_Merdeka")
            {
                surrenderBinding = null;
                surrenderScene = null;
                merdekaOutcomeArmed = false;
            }
            else if (surrenderScene != null && surrenderScene != scene)
            {
                surrenderBinding = null;
                surrenderScene = null;
            }
            if (scene != "B_End")
            {
                karminaBinding = null;
                karminaScene = null;
            }
            else if (karminaScene != null && karminaScene != scene)
            {
                karminaBinding = null;
                karminaScene = null;
            }

            if (!configured || memory == null || abyss == null)
            {
                result.Diagnostic = Append(result.Diagnostic, "reader-unavailable");
                return result;
            }

            result.BoundaryMetadataReady = IsBoundaryMetadataReady(scene);

            int before;
            if (!TryReadInt(Address(abyss.Storage, abyss.Offset), out before))
            {
                result.Diagnostic = Append(result.Diagnostic, "abyss-read-failed");
                return result;
            }

            if (scene == "B_6.6")
            {
                DictionaryRead read = Scan(choices, "bigman", "Larry", result);
                if (read.Available)
                    result.Bigman = read.Found;
            }
            else if (scene == "A_12_Prison")
            {
                DictionaryRead read = Scan(collectibles, "prison", "MerdekaFriend", result);
                if (read.Available)
                    result.Prison = read.Found;
            }

            if (scene == "Office_1")
                ReadStart(result);

            TvStartFrame tvStartFrame = null;
            bool? tvStartActive = null;
            if (scene == "B_Aftermath")
            {
                tvStartActive = TryReadQueryBool(result, out tvStartFrame);
                result.TvStartActive = tvStartActive;
            }
            else if (IsDialogueScene(scene))
                ReadDialogue(result, scene);

            EndingObservation endingObservation = ReadEndingObservations(scene, before, result);

            int after;
            if (!TryReadInt(Address(abyss.Storage, abyss.Offset), out after))
            {
                result.Diagnostic = Append(result.Diagnostic, "abyss-read-failed");
                return result;
            }
            if (before != after)
            {
                result.TvStartActive = null;
                result.Diagnostic = Append(result.Diagnostic, "abyss-mutated");
                return result;
            }

            // The TV signal is committed only after the strong base read has
            // matched before/after.  A transient or mutated read cannot seed a
            // long-lived ending latch in the core.
            if (scene == "B_Aftermath" && tvStartFrame != null
                && tvStartActive == true)
                result.EndingSignals.Add("ending.karmina_tv_start");

            result.MerdekaStage = scene == "B_Merdeka" ? (int?)after : null;
            ObserveMerdekaOutcome(scene, after, result);

            CommitTheodore(endingObservation.Theodore, result, endingObservation);
            CommitKarmina(endingObservation.Karmina, result, endingObservation);
            CommitSurrender(endingObservation.Surrender, result, endingObservation);

            result.GolfBattleActive = endingObservation.GolfBattleActive;
            result.GolfBatteries = endingObservation.GolfBatteries;
            result.GolfCutscenePlaying = endingObservation.GolfCutscenePlaying;
            foreach (string signal in endingObservation.Signals)
                result.EndingSignals.Add(signal);

            ReadCheckpoint(result);

            result.Valid = true;
            result.Abyss = after;
            return result;
        }

        private void ObserveMerdekaOutcome(string scene, int stage, ReadResult result)
        {
            if (scene != "B_Merdeka")
            {
                merdekaOutcomeArmed = false;
                return;
            }
            if (stage == 49)
            {
                merdekaOutcomeArmed = true;
                return;
            }
            if (stage == 51)
            {
                if (merdekaOutcomeArmed)
                    result.EndingSignals.Add("ending.merdeka_defeat");
                merdekaOutcomeArmed = false;
                return;
            }
            if (stage == 52)
            {
                if (merdekaOutcomeArmed)
                    result.EndingSignals.Add("ending.merdeka_victory");
                merdekaOutcomeArmed = false;
                return;
            }
            if (stage == 50)
                return;
            // Unknown stable stages are not evidence of either Merdeka outcome
            // and disarm fail-closed.
            merdekaOutcomeArmed = false;
        }

        private EndingObservation ReadEndingObservations(string scene, int abyssBefore,
            ReadResult result)
        {
            // These signals corroborate a source-confirmed managed producer segment;
            // they are not reads of Unity Animator/GameObject/canvas pixels or an
            // exact rendered-frame boundary.
            EndingObservation observation = new EndingObservation();

            if (scene == "T_Boss")
                observation.Theodore = ReadTheodoreObservation(result);
            if (scene == "B_End")
                observation.Karmina = ReadKarminaObservation(result);
            if (scene == "B_Merdeka")
                observation.Surrender = ReadSurrenderObservation(result);

            if (scene == "Office_3" || scene == "Office_4" || scene == "T_Boss")
            {
                bool cutsceneBefore;
                if (TryReadStaticBool(ending == null ? null : ending.CutscenePlayingField,
                    "ending-cutscene", result, out cutsceneBefore))
                {
                    DictionaryRead choice = scene == "T_Boss"
                        ? ScanValue(choices, "ending-batteries", "Batteries", result)
                        : ScanValue(choices, "ending-office-end", "OfficeEnd", result);
                    bool cutsceneAfter;
                    if (TryReadStaticBool(ending == null ? null : ending.CutscenePlayingField,
                        "ending-cutscene", result, out cutsceneAfter)
                        && cutsceneBefore == cutsceneAfter)
                    {
                        if (scene == "Office_3" || scene == "Office_4")
                        {
                            int expected = scene == "Office_3" ? 2 : 1;
                            if (cutsceneBefore && choice.Available
                                && choice.ValueFound && choice.Value == expected)
                            {
                                observation.Signals.Add(scene == "Office_3"
                                    ? "ending.execution_black" : "ending.family_black");
                            }
                        }
                        else if (choice.Available && choice.ValueFound)
                        {
                            // T_Boss exposes a source-confirmed battle window only
                            // after the value and the two cutscene reads are stable.
                            if (scene == "T_Boss")
                            {
                                observation.GolfBatteries = choice.Value;
                                observation.GolfCutscenePlaying = cutsceneBefore;
                                observation.GolfBattleActive = !cutsceneBefore;
                                // The raw reader candidate remains deliberately broad;
                                // the core requires a prior in-battle decrement.
                                if (cutsceneBefore && choice.Value <= 0)
                                    observation.Signals.Add("ending.theodore_death");
                            }
                        }
                    }
                    else
                    {
                        result.Diagnostic = Append(result.Diagnostic, "ending-cutscene-mutated");
                    }
                }
            }

            // FinalDeath() is a managed coroutine that writes Collectibles["Dead"]
            // while DeadState remains 2/3.  C_End entry is deliberately excluded:
            // it is not the producer and the collectible persists after the jump.
            if (scene != "C_End" && scene != "B_End" && scene != "B_Merdeka")
            {
                int deadStateBefore;
                if (TryReadStaticInt(ending == null ? null : ending.DeadStateField,
                    "ending-deadstate", result, out deadStateBefore)
                    && (deadStateBefore == 2 || deadStateBefore == 3))
                {
                    DictionaryRead dead = Scan(collectibles, "ending-dead", "Dead", result);
                    int deadStateAfter;
                    bool deadStateRead = TryReadStaticInt(ending == null ? null : ending.DeadStateField,
                        "ending-deadstate", result, out deadStateAfter);
                    if (deadStateRead && deadStateBefore == deadStateAfter
                        && dead.Available && dead.Found)
                    {
                        observation.Signals.Add("ending.final_death_black");
                    }
                    else if (deadStateRead && deadStateBefore != deadStateAfter)
                    {
                        result.Diagnostic = Append(result.Diagnostic, "ending-deadstate-mutated");
                    }
                }
            }

            return observation;
        }

        private enum TheodoreSampleStatus
        {
            HardFailure,
            SoftFailure,
            Success
        }

        private KarminaPoll ReadKarminaObservation(ReadResult result)
        {
            KarminaPoll poll = new KarminaPoll();
            if (!IsValidSurrenderLayout(surrender))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "karmina-metadata-unavailable");
                return poll;
            }

            SurrenderFrame first;
            SurrenderFrame second;
            if (!TryReadSurrenderFrame(out first)
                || !TryReadSurrenderFrame(out second)
                || !SameSurrenderFrame(first, second))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "karmina-payload-unavailable");
                return poll;
            }

            poll.Coherent = true;
            poll.Choice = first.Choice;
            result.KarminaChoice = first.Choice;
            bool context = IsKarminaContext(first);
            poll.Context = context;

            bool bindingMatches = karminaBinding != null
                && SameSurrenderBinding(karminaBinding, first);
            if (karminaBinding != null && (!bindingMatches || !context))
            {
                poll.ClearBinding = true;
                result.Diagnostic = Append(result.Diagnostic,
                    !bindingMatches ? "karmina-identity-changed"
                        : "karmina-context-changed");
            }

            if (first.Choice != 0 && first.Choice != 1 && first.Choice != 2)
            {
                poll.ClearBinding = true;
                result.KarminaPhase = "other";
                return poll;
            }

            if (first.Choice == 1 || first.Choice == 2)
            {
                // StartChoice may close the dialogue immediately after either
                // final option. The stable manager/chooser/prompt identity is
                // the commit proof; Playing is intentionally not required here.
                if (bindingMatches && context)
                {
                    poll.Confirmed = true;
                    poll.ClearBinding = true;
                    result.KarminaPhase = "confirmed";
                }
                return poll;
            }

            if (context && first.Playing && first.Line == "B_E/m161"
                && !poll.ClearBinding && karminaBinding == null)
            {
                poll.Capture = BindingFrom(first);
                result.KarminaPhase = "pending";
            }
            else if (bindingMatches && context)
            {
                result.KarminaPhase = "pending";
            }
            return poll;
        }

        private void CommitKarmina(KarminaPoll poll, ReadResult result,
            EndingObservation observation)
        {
            if (poll == null)
                return;
            if (poll.ClearBinding)
                karminaBinding = null;
            if (poll.Capture != null)
            {
                karminaBinding = poll.Capture;
                karminaScene = "B_End";
            }
            if (poll.Coherent && poll.Choice == 0
                && karminaBinding != null && poll.Context)
                result.KarminaPhase = "pending";
            if (poll.Confirmed)
            {
                result.KarminaPhase = "confirmed";
                observation.Signals.Add(KarminaFinalChoiceSignal);
            }
        }

        private static bool IsKarminaContext(SurrenderFrame frame)
        {
            return frame != null && frame.LineCount == 1
                && frame.FirstLine != IntPtr.Zero
                && frame.CurrentLine == 0
                && frame.Line == "B_E/m161"
                && frame.Left == "B_E/m162"
                && frame.Right == "B_E/m163"
                && !frame.Texting;
        }

        private SurrenderPoll ReadSurrenderObservation(ReadResult result)
        {
            SurrenderPoll poll = new SurrenderPoll();
            if (!IsValidSurrenderLayout(surrender))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "surrender-metadata-unavailable");
                return poll;
            }

            SurrenderFrame first;
            SurrenderFrame second;
            if (!TryReadSurrenderFrame(out first)
                || !TryReadSurrenderFrame(out second)
                || !SameSurrenderFrame(first, second))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "surrender-payload-unavailable");
                return poll;
            }

            poll.Coherent = true;
            poll.Choice = first.Choice;
            result.SurrenderChoice = first.Choice;
            bool context = IsSurrenderContext(first);
            poll.Context = context;

            bool bindingMatches = surrenderBinding != null
                && SameSurrenderBinding(surrenderBinding, first);
            if (surrenderBinding != null && (!bindingMatches || !context))
            {
                poll.ClearBinding = true;
                result.Diagnostic = Append(result.Diagnostic,
                    !bindingMatches ? "surrender-identity-changed"
                        : "surrender-context-changed");
            }

            if (first.Choice == 2 || (first.Choice != 0 && first.Choice != 1))
            {
                poll.ClearBinding = true;
                result.SurrenderPhase = "other";
                return poll;
            }

            if (first.Choice == 1)
            {
                // StartChoice clears Playing immediately after the click, so the
                // confirmation does not require Playing to remain true. The
                // manager/chooser/context identity is still required.
                if (bindingMatches && context)
                {
                    poll.Confirmed = true;
                    poll.ClearBinding = true;
                    result.SurrenderPhase = "confirmed";
                }
                return poll;
            }

            if (context && first.Playing && first.Line == "B_M/m45"
                && !poll.ClearBinding && surrenderBinding == null)
            {
                poll.Capture = BindingFrom(first);
                result.SurrenderPhase = "pending";
            }
            else if (bindingMatches && context)
            {
                result.SurrenderPhase = "pending";
            }
            return poll;
        }

        private void CommitSurrender(SurrenderPoll poll, ReadResult result,
            EndingObservation observation)
        {
            if (poll == null)
                return;
            if (poll.ClearBinding)
                surrenderBinding = null;
            if (poll.Capture != null)
            {
                surrenderBinding = poll.Capture;
                surrenderScene = "B_Merdeka";
            }
            if (poll.Coherent && poll.Choice == 0
                && surrenderBinding != null && poll.Context)
                result.SurrenderPhase = "pending";
            if (poll.Confirmed)
            {
                result.SurrenderPhase = "confirmed";
                observation.Signals.Add(MerdekaSurrenderSignal);
            }
        }

        private SurrenderBinding BindingFrom(SurrenderFrame frame)
        {
            return new SurrenderBinding {
                Dialogue = frame.Dialogue,
                DialogueClass = frame.DialogueClass,
                ButtonChoose = frame.ButtonChoose,
                ButtonChooseClass = frame.ButtonChooseClass,
                Lines = frame.Lines,
                FirstLine = frame.FirstLine,
                LeftString = frame.LeftString,
                RightString = frame.RightString
            };
        }

        private bool TryReadSurrenderFrame(out SurrenderFrame frame)
        {
            frame = null;
            if (!IsValidSurrenderLayout(surrender))
                return false;

            IntPtr dialogueObject;
            if (!TryReadPointer(Address(surrender.InstanceField.Storage,
                surrender.InstanceField.Offset), out dialogueObject)
                || dialogueObject == IntPtr.Zero)
                return false;
            IntPtr dialogueClass;
            if (!TryReadRuntimeClass(dialogueObject, surrender.TestDirectClassIdentity,
                out dialogueClass)
                || dialogueClass != surrender.DialogueClassAddress)
                return false;

            IntPtr chooser;
            if (!TryReadPointer(Address(dialogueObject, surrender.ButtonChooseOffset),
                out chooser) || chooser == IntPtr.Zero)
                return false;
            IntPtr chooserClass;
            if (!TryReadRuntimeClass(chooser, surrender.TestDirectClassIdentity,
                out chooserClass)
                || chooserClass != surrender.ButtonChooseClassAddress)
                return false;

            IntPtr lines;
            int lineCount;
            IntPtr firstLine = IntPtr.Zero;
            string line = null;
            int currentLine;
            IntPtr leftString;
            string left;
            IntPtr rightString;
            string right;
            bool playing;
            bool texting;
            int choice;
            if (!TryReadPointer(Address(dialogueObject, surrender.DialogueLinesOffset),
                    out lines)
                || lines == IntPtr.Zero
                || !TryReadInt(Address(lines, ArrayLengthOffset), out lineCount)
                || lineCount < 0 || lineCount > MaxDialogueLineCount
                || (lineCount > 0
                    && (!TryReadPointer(Address(lines, ArrayDataOffset), out firstLine)
                        || firstLine == IntPtr.Zero
                        || !TryReadString(firstLine, out line)
                        || line == null))
                || !TryReadInt(Address(dialogueObject, surrender.CurrentLineOffset),
                    out currentLine)
                || !TryReadPointer(Address(dialogueObject, surrender.LeftStringOffset),
                    out leftString)
                || leftString == IntPtr.Zero
                || !TryReadString(leftString, out left) || left == null
                || !TryReadPointer(Address(dialogueObject, surrender.RightStringOffset),
                    out rightString)
                || rightString == IntPtr.Zero
                || !TryReadString(rightString, out right) || right == null
                || !TryReadBool(Address(dialogueObject, surrender.PlayingOffset),
                    out playing)
                || !TryReadBool(Address(dialogueObject, surrender.TextingOffset),
                    out texting)
                || !TryReadInt(Address(chooser, surrender.ButtonChoiceOffset),
                    out choice))
                return false;

            IntPtr dialogueAfter;
            IntPtr chooserAfter;
            IntPtr dialogueClassAfter;
            IntPtr chooserClassAfter;
            IntPtr linesAfter;
            int lineCountAfter;
            IntPtr firstLineAfter = IntPtr.Zero;
            string lineAfter = null;
            int currentLineAfter;
            IntPtr leftStringAfter;
            string leftAfter;
            IntPtr rightStringAfter;
            string rightAfter;
            bool playingAfter;
            bool textingAfter;
            int choiceAfter;
            if (!TryReadPointer(Address(surrender.InstanceField.Storage,
                    surrender.InstanceField.Offset), out dialogueAfter)
                || !TryReadRuntimeClass(dialogueAfter, surrender.TestDirectClassIdentity,
                    out dialogueClassAfter)
                || !TryReadPointer(Address(dialogueAfter, surrender.ButtonChooseOffset),
                    out chooserAfter)
                || !TryReadRuntimeClass(chooserAfter, surrender.TestDirectClassIdentity,
                    out chooserClassAfter)
                || !TryReadPointer(Address(dialogueAfter, surrender.DialogueLinesOffset),
                    out linesAfter)
                || linesAfter == IntPtr.Zero
                || !TryReadInt(Address(linesAfter, ArrayLengthOffset), out lineCountAfter)
                || (lineCountAfter > 0
                    && (!TryReadPointer(Address(linesAfter, ArrayDataOffset),
                            out firstLineAfter)
                        || firstLineAfter == IntPtr.Zero
                        || !TryReadString(firstLineAfter, out lineAfter)
                        || lineAfter == null))
                || !TryReadInt(Address(dialogueAfter, surrender.CurrentLineOffset),
                    out currentLineAfter)
                || !TryReadPointer(Address(dialogueAfter, surrender.LeftStringOffset),
                    out leftStringAfter)
                || leftStringAfter == IntPtr.Zero
                || !TryReadString(leftStringAfter, out leftAfter) || leftAfter == null
                || !TryReadPointer(Address(dialogueAfter, surrender.RightStringOffset),
                    out rightStringAfter)
                || rightStringAfter == IntPtr.Zero
                || !TryReadString(rightStringAfter, out rightAfter) || rightAfter == null
                || !TryReadBool(Address(dialogueAfter, surrender.PlayingOffset),
                    out playingAfter)
                || !TryReadBool(Address(dialogueAfter, surrender.TextingOffset),
                    out textingAfter)
                || !TryReadInt(Address(chooserAfter, surrender.ButtonChoiceOffset),
                    out choiceAfter))
                return false;

            if (dialogueAfter != dialogueObject || chooserAfter != chooser
                || dialogueClassAfter != dialogueClass
                || chooserClassAfter != chooserClass
                || linesAfter != lines || lineCountAfter != lineCount
                || firstLineAfter != firstLine || lineAfter != line
                || currentLineAfter != currentLine
                || leftStringAfter != leftString || leftAfter != left
                || rightStringAfter != rightString || rightAfter != right
                || playingAfter != playing || textingAfter != texting
                || choiceAfter != choice)
                return false;

            frame = new SurrenderFrame {
                Dialogue = dialogueObject,
                DialogueClass = dialogueClass,
                ButtonChoose = chooser,
                ButtonChooseClass = chooserClass,
                Lines = lines,
                FirstLine = firstLine,
                Line = line,
                LineCount = lineCount,
                CurrentLine = currentLine,
                LeftString = leftString,
                Left = left,
                RightString = rightString,
                Right = right,
                Playing = playing,
                Texting = texting,
                Choice = choice
            };
            return true;
        }

        private static bool IsSurrenderContext(SurrenderFrame frame)
        {
            return frame != null && frame.LineCount == 1
                && frame.FirstLine != IntPtr.Zero
                && frame.CurrentLine == 0
                && frame.Line == "B_M/m45"
                && frame.Left == "B_M/m41"
                && frame.Right == "B_M/m42"
                && !frame.Texting;
        }

        private static bool SameSurrenderFrame(SurrenderFrame first,
            SurrenderFrame second)
        {
            return first != null && second != null
                && first.Dialogue == second.Dialogue
                && first.DialogueClass == second.DialogueClass
                && first.ButtonChoose == second.ButtonChoose
                && first.ButtonChooseClass == second.ButtonChooseClass
                && first.Lines == second.Lines
                && first.FirstLine == second.FirstLine
                && first.Line == second.Line
                && first.LineCount == second.LineCount
                && first.CurrentLine == second.CurrentLine
                && first.LeftString == second.LeftString
                && first.Left == second.Left
                && first.RightString == second.RightString
                && first.Right == second.Right
                && first.Playing == second.Playing
                && first.Texting == second.Texting
                && first.Choice == second.Choice;
        }

        private static bool SameSurrenderBinding(SurrenderBinding binding,
            SurrenderFrame frame)
        {
            return binding != null && frame != null
                && binding.Dialogue == frame.Dialogue
                && binding.DialogueClass == frame.DialogueClass
                && binding.ButtonChoose == frame.ButtonChoose
                && binding.ButtonChooseClass == frame.ButtonChooseClass
                && binding.Lines == frame.Lines
                && binding.FirstLine == frame.FirstLine
                && binding.LeftString == frame.LeftString
                && binding.RightString == frame.RightString;
        }

        private TheodorePoll ReadTheodoreObservation(ReadResult result)
        {
            TheodorePoll poll = new TheodorePoll();
            if (!IsValidTheodoreLayout(theodore))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-metadata-unavailable");
                return poll;
            }

            bool cutsceneBefore;
            if (!TryReadStaticBool(ending == null ? null : ending.CutscenePlayingField,
                "theodore-cutscene", result, out cutsceneBefore))
                return poll;

            TheodoreRegistry registry;
            if (!TryReadTheodoreRegistry(result, out registry))
            {
                poll.ClearCache = theodoreBinding != null;
                return poll;
            }

            IntPtr objectPointer = IntPtr.Zero;
            int candidateIndex = -1;
            bool verifyCurrentIndex = false;
            TheodoreBinding expected = null;
            if (!cutsceneBefore)
            {
                if (registry.ExactSliderCount != 1)
                {
                    result.Diagnostic = Append(result.Diagnostic,
                        "theodore-registry-not-sole-slider");
                    poll.ClearCache = theodoreBinding != null;
                    return poll;
                }
                objectPointer = registry.Candidate;
                candidateIndex = registry.CandidateIndex;
                verifyCurrentIndex = true;
                if (theodoreBinding != null
                    && theodoreBinding.Object != objectPointer)
                    poll.ClearCache = true;
                if (theodoreBinding != null
                    && theodoreBinding.Object == objectPointer)
                    expected = theodoreBinding;
            }
            else
            {
                if (theodoreBinding == null)
                    return poll;
                if (registry.ExactSliderCount > 1
                    || (registry.ExactSliderCount == 1
                        && registry.Candidate != theodoreBinding.Object))
                {
                    result.Diagnostic = Append(result.Diagnostic,
                        "theodore-registry-identity-changed");
                    poll.ClearCache = true;
                    return poll;
                }
                objectPointer = theodoreBinding.Object;
                expected = theodoreBinding;
                if (registry.ExactSliderCount == 1)
                {
                    candidateIndex = registry.CandidateIndex;
                    verifyCurrentIndex = true;
                }
            }

            TheodoreBinding sampleBinding;
            int hp;
            TheodoreSampleStatus status = TryReadStableTheodoreSlider(objectPointer,
                candidateIndex, verifyCurrentIndex, expected, out sampleBinding, out hp);
            if (status == TheodoreSampleStatus.HardFailure)
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-slider-identity-invalid");
                poll.ClearCache = theodoreBinding != null;
                return poll;
            }
            if (status == TheodoreSampleStatus.SoftFailure)
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-hp-unknown");
                return poll;
            }

            bool cutsceneAfter;
            if (!TryReadStaticBool(ending == null ? null : ending.CutscenePlayingField,
                "theodore-cutscene", result, out cutsceneAfter))
                return poll;
            if (cutsceneAfter != cutsceneBefore)
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-cutscene-mutated");
                return poll;
            }

            poll.Coherent = true;
            poll.Hp = hp;
            if (cutsceneBefore)
            {
                // A zero is terminal only for an already retained positive
                // binding. A cutscene-start sample never binds a new object.
                poll.Terminal = hp == 0 && theodoreBinding != null;
            }
            else if (theodoreBinding == null
                || theodoreBinding.Object != objectPointer)
            {
                // A first zero is observable but cannot arm Ending 9.
                if (hp > 0)
                    poll.Capture = sampleBinding;
            }
            return poll;
        }

        private void CommitTheodore(TheodorePoll poll, ReadResult result,
            EndingObservation observation)
        {
            if (poll == null)
                return;
            if (poll.ClearCache)
                theodoreBinding = null;
            if (poll.Capture != null)
                theodoreBinding = poll.Capture;
            if (poll.Coherent)
                result.TheodoreHp = poll.Hp;
            if (poll.Terminal && theodoreBinding != null)
                observation.Signals.Add("ending.theodore_victory");
        }

        private bool IsSelectableRuntimeClass(IntPtr runtimeClass)
        {
            if (runtimeClass == theodore.SliderClassAddress
                || runtimeClass == theodore.SelectableClassAddress)
                return true;
            if (theodore.ProvenSelectableClasses.Contains(runtimeClass))
                return true;
            if (theodore.ClassParent == null)
                return false;
            try
            {
                HashSet<IntPtr> visited = new HashSet<IntPtr>();
                IntPtr current = runtimeClass;
                for (int depth = 0; depth < 32; depth++)
                {
                    if (!IsValidMetadataPointer(current) || !visited.Add(current))
                        return false;
                    if (current == theodore.SelectableClassAddress)
                    {
                        // Mono class ancestry is immutable for this configured
                        // process/domain. A new reader owns a new bounded cache.
                        if (theodore.ProvenSelectableClasses.Count < MaxTheodoreSelectableCount)
                            theodore.ProvenSelectableClasses.Add(runtimeClass);
                        return true;
                    }
                    current = theodore.ClassParent(current);
                }
            }
            catch (Exception)
            {
                // Unknown ancestry is not proof of a Selectable slot.
            }
            return false;
        }

        private bool TryReadTheodoreRegistry(ReadResult result,
            out TheodoreRegistry registry)
        {
            registry = null;
            IntPtr array;
            int count;
            if (!TryReadPointer(Address(theodore.SelectablesField.Storage,
                theodore.SelectablesField.Offset), out array)
                || !TryReadInt(Address(theodore.SelectableCountField.Storage,
                    theodore.SelectableCountField.Offset), out count))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-registry-read-failed");
                return false;
            }
            if (count < 0 || count > MaxTheodoreSelectableCount)
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-registry-unavailable");
                return false;
            }

            int length = 0;
            if (array != IntPtr.Zero
                && (!TryReadInt(Address(array, ArrayLengthOffset), out length)
                    || length < 0 || length > MaxTheodoreSelectableCount
                    || count > length))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-registry-unavailable");
                return false;
            }
            if (array == IntPtr.Zero && count != 0)
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-registry-unavailable");
                return false;
            }

            List<IntPtr> slots = new List<IntPtr>(count);
            List<IntPtr> classes = new List<IntPtr>(count);
            TheodoreRegistry observed = new TheodoreRegistry {
                Candidate = IntPtr.Zero, CandidateIndex = -1, ExactSliderCount = 0
            };
            for (int i = 0; i < count; i++)
            {
                IntPtr slot;
                if (!TryReadPointer(Address(Address(array, ArrayDataOffset),
                    checked(i * PointerSize)), out slot) || slot == IntPtr.Zero)
                {
                    result.Diagnostic = Append(result.Diagnostic,
                        "theodore-registry-slot-invalid");
                    return false;
                }
                IntPtr runtimeClass;
                if (!TryReadRuntimeClass(slot, out runtimeClass))
                {
                    result.Diagnostic = Append(result.Diagnostic,
                        "theodore-registry-class-invalid");
                    return false;
                }
                if (!IsSelectableRuntimeClass(runtimeClass))
                {
                    result.Diagnostic = Append(result.Diagnostic,
                        "theodore-registry-not-selectable");
                    return false;
                }
                slots.Add(slot);
                classes.Add(runtimeClass);
                if (runtimeClass == theodore.SliderClassAddress)
                {
                    observed.ExactSliderCount++;
                    observed.Candidate = slot;
                    observed.CandidateIndex = i;
                }
            }

            IntPtr arrayAfter;
            int countAfter;
            if (!TryReadPointer(Address(theodore.SelectablesField.Storage,
                theodore.SelectablesField.Offset), out arrayAfter)
                || !TryReadInt(Address(theodore.SelectableCountField.Storage,
                    theodore.SelectableCountField.Offset), out countAfter)
                || arrayAfter != array || countAfter != count)
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "theodore-registry-mutated");
                return false;
            }
            if (array != IntPtr.Zero)
            {
                int lengthAfter;
                if (!TryReadInt(Address(array, ArrayLengthOffset), out lengthAfter)
                    || lengthAfter != length)
                {
                    result.Diagnostic = Append(result.Diagnostic,
                        "theodore-registry-mutated");
                    return false;
                }
            }
            for (int i = 0; i < slots.Count; i++)
            {
                IntPtr slotAfter;
                if (!TryReadPointer(Address(Address(array, ArrayDataOffset),
                    checked(i * PointerSize)), out slotAfter)
                    || slotAfter != slots[i])
                {
                    result.Diagnostic = Append(result.Diagnostic,
                        "theodore-registry-mutated");
                    return false;
                }
                IntPtr classAfter;
                if (!TryReadRuntimeClass(slotAfter, out classAfter)
                    || classAfter != classes[i])
                {
                    result.Diagnostic = Append(result.Diagnostic,
                        "theodore-registry-mutated");
                    return false;
                }
            }
            registry = observed;
            return true;
        }

        private TheodoreSampleStatus TryReadStableTheodoreSlider(IntPtr objectPointer,
            int candidateIndex, bool verifyCurrentIndex, TheodoreBinding expected,
            out TheodoreBinding binding, out int hp)
        {
            binding = null;
            hp = 0;
            if (objectPointer == IntPtr.Zero)
                return TheodoreSampleStatus.HardFailure;

            TheodoreFingerprint first;
            if (!TryReadTheodoreFingerprint(objectPointer, out first))
                return TheodoreSampleStatus.HardFailure;
            if (expected != null && (expected.Image != theodore.ImageAddress
                || expected.Object != objectPointer
                || expected.Class != first.Class
                || expected.Native != first.Native
                || expected.FillRect != first.FillRect))
                return TheodoreSampleStatus.HardFailure;
            if (verifyCurrentIndex && first.HasCurrentIndex
                && first.CurrentIndex != candidateIndex)
                return TheodoreSampleStatus.HardFailure;

            int firstBits;
            if (!TryReadTheodoreHp(objectPointer, out hp, out firstBits))
                return TheodoreSampleStatus.SoftFailure;

            TheodoreFingerprint second;
            if (!TryReadTheodoreFingerprint(objectPointer, out second)
                || !SameTheodoreFingerprint(first, second))
                return TheodoreSampleStatus.HardFailure;
            int secondHp;
            int secondBits;
            if (!TryReadTheodoreHp(objectPointer, out secondHp, out secondBits))
                return TheodoreSampleStatus.SoftFailure;
            if (firstBits != secondBits)
                return TheodoreSampleStatus.SoftFailure;
            if (verifyCurrentIndex && second.HasCurrentIndex
                && second.CurrentIndex != candidateIndex)
                return TheodoreSampleStatus.HardFailure;

            binding = new TheodoreBinding {
                Object = objectPointer,
                Native = first.Native,
                Class = first.Class,
                Image = theodore.ImageAddress,
                FillRect = first.FillRect
            };
            hp = secondHp;
            return TheodoreSampleStatus.Success;
        }

        private bool TryReadTheodoreFingerprint(IntPtr objectPointer,
            out TheodoreFingerprint fingerprint)
        {
            fingerprint = null;
            IntPtr runtimeClass;
            if (!TryReadRuntimeClass(objectPointer, out runtimeClass)
                || runtimeClass != theodore.SliderClassAddress)
                return false;

            IntPtr native;
            IntPtr fillRect;
            IntPtr handleRect;
            IntPtr targetGraphic;
            if (!TryReadPointer(Address(objectPointer, theodore.CachedPtrOffset),
                out native)
                || native == IntPtr.Zero
                || !TryReadPointer(Address(objectPointer, theodore.FillRectOffset),
                    out fillRect)
                || fillRect == IntPtr.Zero
                || !TryReadPointer(Address(objectPointer, theodore.HandleRectOffset),
                    out handleRect)
                || !TryReadPointer(Address(objectPointer, theodore.TargetGraphicOffset),
                    out targetGraphic))
                return false;

            IntPtr fillClass;
            if (!TryReadRuntimeClass(fillRect, out fillClass)
                || fillClass != theodore.RectTransformClassAddress
                || handleRect != IntPtr.Zero || targetGraphic != IntPtr.Zero)
                return false;

            int navigationMode;
            int direction;
            if (!TryReadInt(Address(Address(objectPointer, theodore.NavigationOffset),
                theodore.NavigationModeOffset), out navigationMode)
                || !TryReadInt(Address(objectPointer, theodore.DirectionOffset),
                    out direction)
                || navigationMode != 0 || direction != 0)
                return false;

            int minBits;
            int maxBits;
            float min;
            float max;
            if (!TryReadFloatBits(Address(objectPointer, theodore.MinValueOffset),
                out min, out minBits)
                || !TryReadFloatBits(Address(objectPointer, theodore.MaxValueOffset),
                    out max, out maxBits)
                || min != 0f || max != 100f)
                return false;

            bool wholeNumbers;
            if (!TryReadBool(Address(objectPointer, theodore.WholeNumbersOffset),
                out wholeNumbers) || !wholeNumbers)
                return false;

            TheodoreFingerprint result = new TheodoreFingerprint {
                Class = runtimeClass,
                Native = native,
                FillRect = fillRect,
                HandleRect = handleRect,
                TargetGraphic = targetGraphic,
                NavigationMode = navigationMode,
                Direction = direction,
                Min = min,
                Max = max,
                WholeNumbers = wholeNumbers,
                HasCurrentIndex = theodore.CurrentIndexOffset >= 0,
                CurrentIndex = 0
            };
            if (result.HasCurrentIndex
                && !TryReadInt(Address(objectPointer, theodore.CurrentIndexOffset),
                    out result.CurrentIndex))
                return false;
            fingerprint = result;
            return true;
        }

        private bool TryReadTheodoreHp(IntPtr objectPointer, out int hp,
            out int bits)
        {
            hp = 0;
            bits = 0;
            if (!TryReadInt(Address(objectPointer, theodore.ValueOffset), out bits))
                return false;
            float value;
            try
            {
                value = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            }
            catch (Exception)
            {
                return false;
            }
            if (Single.IsNaN(value) || Single.IsInfinity(value)
                || value < 0f || value > 100f || value != (int)value)
                return false;
            int integral = (int)value;
            if (integral % 5 != 0)
                return false;
            hp = integral;
            return true;
        }

        private bool TryReadFloatBits(IntPtr address, out float value,
            out int bits)
        {
            value = 0f;
            bits = 0;
            if (!TryReadInt(address, out bits))
                return false;
            try
            {
                value = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                return !Single.IsNaN(value) && !Single.IsInfinity(value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SameTheodoreFingerprint(TheodoreFingerprint first,
            TheodoreFingerprint second)
        {
            return first != null && second != null
                && first.Class == second.Class
                && first.Native == second.Native
                && first.FillRect == second.FillRect
                && first.HandleRect == second.HandleRect
                && first.TargetGraphic == second.TargetGraphic
                && first.NavigationMode == second.NavigationMode
                && first.Direction == second.Direction
                && first.Min == second.Min
                && first.Max == second.Max
                && first.WholeNumbers == second.WholeNumbers
                && first.HasCurrentIndex == second.HasCurrentIndex
                && (!first.HasCurrentIndex || first.CurrentIndex == second.CurrentIndex);
        }

        private bool TryReadRuntimeClass(IntPtr objectPointer,
            out IntPtr runtimeClass)
        {
            return TryReadRuntimeClass(objectPointer,
                theodore != null && theodore.TestDirectClassIdentity,
                out runtimeClass);
        }

        private bool TryReadRuntimeClass(IntPtr objectPointer, bool allowDirectIdentity,
            out IntPtr runtimeClass)
        {
            runtimeClass = IntPtr.Zero;
            if (objectPointer == IntPtr.Zero)
                return false;
            IntPtr vtable;
            if (!TryReadPointer(Address(objectPointer, 0), out vtable)
                || !IsValidMetadataPointer(vtable))
                return false;
            if (TryReadPointer(Address(vtable, 0), out runtimeClass)
                && IsValidMetadataPointer(runtimeClass))
                return true;
            // The offline fixture can store the qualified runtime class directly
            // in the first word. Production Configure layouts never enable this;
            // live reads always require MonoObject.vtable->MonoVTable.klass.
            if (allowDirectIdentity)
            {
                runtimeClass = vtable;
                return runtimeClass != IntPtr.Zero;
            }
            runtimeClass = IntPtr.Zero;
            return false;
        }

        private bool TryReadStaticBool(StaticField field, string diagnosticName,
            ReadResult result, out bool value)
        {
            value = false;
            if (!IsValidStaticField(field, 1))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-unavailable");
                return false;
            }
            if (!TryReadBool(Address(field.Storage, field.Offset), out value))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                return false;
            }
            return true;
        }

        private bool TryReadStaticInt(StaticField field, string diagnosticName,
            ReadResult result, out int value)
        {
            value = 0;
            if (!IsValidStaticField(field, 4))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-unavailable");
                return false;
            }
            if (!TryReadInt(Address(field.Storage, field.Offset), out value))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                return false;
            }
            return true;
        }

        private void ReadCheckpoint(ReadResult result)
        {
            if (checkpoint == null || !IsValidStaticField(checkpoint, PointerSize))
            {
                result.Diagnostic = Append(result.Diagnostic, "checkpoint-unavailable");
                return;
            }

            IntPtr first;
            if (!TryReadPointer(Address(checkpoint.Storage, checkpoint.Offset), out first))
            {
                result.Diagnostic = Append(result.Diagnostic, "checkpoint-read-failed");
                return;
            }
            if (first == IntPtr.Zero)
            {
                // A null first read is only a stable null after the second read.
                IntPtr secondNull;
                if (!TryReadPointer(Address(checkpoint.Storage, checkpoint.Offset), out secondNull))
                {
                    result.Diagnostic = Append(result.Diagnostic, "checkpoint-read-failed");
                    return;
                }
                if (secondNull != IntPtr.Zero)
                {
                    result.Diagnostic = Append(result.Diagnostic, "checkpoint-mutated");
                    return;
                }
                result.HasCheckpoint = false;
                return;
            }

            string checkpointScene;
            if (!TryReadString(first, out checkpointScene) || checkpointScene == null)
            {
                result.Diagnostic = Append(result.Diagnostic, "checkpoint-malformed");
                return;
            }

            IntPtr second;
            if (!TryReadPointer(Address(checkpoint.Storage, checkpoint.Offset), out second))
            {
                result.Diagnostic = Append(result.Diagnostic, "checkpoint-read-failed");
                return;
            }
            if (second != first)
            {
                result.Diagnostic = Append(result.Diagnostic, "checkpoint-mutated");
                return;
            }
            result.HasCheckpoint = true;
        }

        private void ReadStart(ReadResult result)
        {
            if (!IsValidStartLayout(start))
            {
                result.Diagnostic = Append(result.Diagnostic, "start-metadata-unavailable");
                return;
            }

            bool cutsceneBefore;
            if (!TryReadBool(Address(start.CutscenePlayingField.Storage,
                start.CutscenePlayingField.Offset), out cutsceneBefore))
            {
                result.Diagnostic = Append(result.Diagnostic, "start-cutscene-read-failed");
                return;
            }

            IntPtr managerBefore;
            if (!TryReadPointer(Address(start.InstanceField.Storage,
                start.InstanceField.Offset), out managerBefore))
            {
                result.Diagnostic = Append(result.Diagnostic, "start-instance-read-failed");
                return;
            }
            if (managerBefore == IntPtr.Zero)
            {
                result.Diagnostic = Append(result.Diagnostic, "start-instance-unavailable");
                return;
            }

            IntPtr playerBefore;
            if (!TryReadPointer(Address(managerBefore, start.PlayerOffset), out playerBefore)
                || playerBefore == IntPtr.Zero)
            {
                result.Diagnostic = Append(result.Diagnostic, "start-player-unavailable");
                return;
            }
            bool canMove;
            if (!TryReadBool(Address(playerBefore, start.CanMoveOffset), out canMove))
            {
                result.Diagnostic = Append(result.Diagnostic, "start-canmove-read-failed");
                return;
            }

            IntPtr playerAfter;
            IntPtr managerAfter;
            bool canMoveAfter;
            bool cutsceneAfter;
            if (!TryReadPointer(Address(managerBefore, start.PlayerOffset), out playerAfter)
                || !TryReadBool(Address(playerBefore, start.CanMoveOffset), out canMoveAfter)
                || !TryReadPointer(Address(start.InstanceField.Storage,
                    start.InstanceField.Offset), out managerAfter)
                || !TryReadBool(Address(start.CutscenePlayingField.Storage,
                    start.CutscenePlayingField.Offset), out cutsceneAfter))
            {
                result.Diagnostic = Append(result.Diagnostic, "start-reread-failed");
                return;
            }
            if (playerAfter != playerBefore || managerAfter != managerBefore
                || canMoveAfter != canMove || cutsceneAfter != cutsceneBefore)
            {
                result.Diagnostic = Append(result.Diagnostic, "start-mutated");
                return;
            }
            result.StartReady = !cutsceneBefore && canMove;
        }

        private bool? TryReadQueryBool(ReadResult result, out TvStartFrame frame)
        {
            frame = null;
            if (!IsValidTvStartLayout(tvStart))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "tv-start-metadata-unavailable");
                return null;
            }

            if (!TryReadTvStartFrame(out frame))
            {
                result.Diagnostic = Append(result.Diagnostic,
                    "tv-start-payload-unavailable");
                return null;
            }

            // A coherent managed frame is a valid false unless it is exactly
            // the producer's one-line m36 television start.
            return frame.LineCount == 1
                && frame.FirstLine != IntPtr.Zero
                && frame.CurrentLine == 0
                && frame.Line == "B_A/m36"
                && frame.Playing;
        }

        private static void SetReadResultTvStartMetadataReady(ReadResult result,
            bool ready)
        {
            if (result != null)
                result.TvStartMetadataReady = ready;
        }

        private bool TryReadTvStartFrame(out TvStartFrame frame)
        {
            frame = null;
            IntPtr firstObject;
            TvStartFrame first;
            if (!TryReadPointer(Address(tvStart.InstanceStaticAddress.Storage,
                    tvStart.InstanceStaticAddress.Offset), out firstObject)
                || firstObject == IntPtr.Zero
                || !TryReadTvStartPayload(firstObject, out first))
                return false;

            IntPtr secondObject;
            TvStartFrame second;
            if (!TryReadPointer(Address(tvStart.InstanceStaticAddress.Storage,
                    tvStart.InstanceStaticAddress.Offset), out secondObject)
                || secondObject != firstObject
                || !TryReadTvStartPayload(secondObject, out second)
                || !SameTvStartFrame(first, second))
                return false;

            frame = first;
            return true;
        }

        private bool TryReadTvStartPayload(IntPtr dialogueObject,
            out TvStartFrame frame)
        {
            frame = null;
            IntPtr dialogueClass;
            if (!TryReadRuntimeClass(dialogueObject, tvStart.TestDirectClassIdentity,
                    out dialogueClass)
                || dialogueClass != tvStart.DialogueClassAddress)
                return false;

            IntPtr lines;
            int lineCount;
            if (!TryReadPointer(Address(dialogueObject, tvStart.LinesOffset), out lines)
                || lines == IntPtr.Zero
                || !TryReadInt(Address(lines, ArrayLengthOffset), out lineCount)
                || lineCount < 0 || lineCount > MaxDialogueLineCount)
                return false;

            IntPtr firstLine = IntPtr.Zero;
            string line = null;
            if (lineCount > 0
                && (!TryReadPointer(Address(lines, ArrayDataOffset), out firstLine)
                    || firstLine == IntPtr.Zero
                    || !TryReadString(firstLine, out line)
                    || line == null))
                return false;

            int currentLine;
            bool playing;
            if (!TryReadInt(Address(dialogueObject, tvStart.CurrentLineOffset),
                    out currentLine)
                || !TryReadBool(Address(dialogueObject, tvStart.PlayingOffset),
                    out playing))
                return false;

            frame = new TvStartFrame {
                Dialogue = dialogueObject,
                DialogueClass = dialogueClass,
                Lines = lines,
                LineCount = lineCount,
                FirstLine = firstLine,
                Line = line,
                CurrentLine = currentLine,
                Playing = playing
            };
            return true;
        }

        private static bool SameTvStartFrame(TvStartFrame first,
            TvStartFrame second)
        {
            return first != null && second != null
                && first.Dialogue == second.Dialogue
                && first.DialogueClass == second.DialogueClass
                && first.Lines == second.Lines
                && first.LineCount == second.LineCount
                && first.FirstLine == second.FirstLine
                && first.Line == second.Line
                && first.CurrentLine == second.CurrentLine
                && first.Playing == second.Playing;
        }

        private void ReadDialogue(ReadResult result, string scene)
        {
            if (!IsValidDialogueLayout(dialogue))
            {
                result.Diagnostic = Append(result.Diagnostic, "dialogue-metadata-unavailable");
                return;
            }

            IntPtr first;
            if (!TryReadPointer(Address(dialogue.InstanceField.Storage,
                dialogue.InstanceField.Offset), out first))
            {
                result.Diagnostic = Append(result.Diagnostic, "dialogue-instance-read-failed");
                return;
            }
            if (first == IntPtr.Zero)
            {
                result.Diagnostic = Append(result.Diagnostic, "dialogue-instance-unavailable");
                return;
            }

            bool playing;
            if (!TryReadBool(Address(first, dialogue.PlayingOffset), out playing))
            {
                result.Diagnostic = Append(result.Diagnostic, "dialogue-playing-read-failed");
                return;
            }

            // Keep the old Playing-only observation compatible with existing
            // callers, but never emit a physical ending without the payload and
            // visibility fields below.
            if (!IsFullDialogueLayout(dialogue))
            {
                IntPtr legacySecond;
                if (!TryReadPointer(Address(dialogue.InstanceField.Storage,
                    dialogue.InstanceField.Offset), out legacySecond))
                {
                    result.Diagnostic = Append(result.Diagnostic, "dialogue-instance-read-failed");
                    return;
                }
                if (legacySecond != first)
                {
                    result.Diagnostic = Append(result.Diagnostic, "dialogue-mutated");
                    return;
                }
                result.DialoguePlaying = playing;
                result.Diagnostic = Append(result.Diagnostic, "dialogue-payload-metadata-unavailable");
                return;
            }

            IntPtr lines;
            int lineCount;
            IntPtr firstLine;
            string line;
            int currentLine;
            IntPtr textBox;
            int visible;
            if (!TryReadPointer(Address(first, dialogue.LinesOffset), out lines)
                || lines == IntPtr.Zero
                || !TryReadInt(Address(lines, ArrayLengthOffset), out lineCount)
                || lineCount < 1 || lineCount > MaxDialogueLineCount
                || !TryReadPointer(Address(lines, ArrayDataOffset), out firstLine)
                || firstLine == IntPtr.Zero
                || !TryReadString(firstLine, out line) || line == null
                || !TryReadInt(Address(first, dialogue.CurrentLineOffset), out currentLine)
                || !TryReadPointer(Address(first, dialogue.TextBoxOffset), out textBox)
                || textBox == IntPtr.Zero
                || !TryReadInt(Address(textBox, dialogue.VisibleCharactersOffset), out visible))
            {
                result.Diagnostic = Append(result.Diagnostic, "dialogue-payload-read-failed");
                return;
            }

            bool playingAfter;
            int currentLineAfter;
            int visibleAfter;
            IntPtr linesAfter;
            int lineCountAfter;
            IntPtr firstLineAfter;
            string lineAfter;
            IntPtr textBoxAfter;
            IntPtr second;
            if (!TryReadBool(Address(first, dialogue.PlayingOffset), out playingAfter)
                || !TryReadInt(Address(first, dialogue.CurrentLineOffset), out currentLineAfter)
                || !TryReadPointer(Address(first, dialogue.LinesOffset), out linesAfter)
                || linesAfter == IntPtr.Zero
                || !TryReadInt(Address(linesAfter, ArrayLengthOffset), out lineCountAfter)
                || !TryReadPointer(Address(linesAfter, ArrayDataOffset), out firstLineAfter)
                || firstLineAfter == IntPtr.Zero
                || !TryReadString(firstLineAfter, out lineAfter) || lineAfter == null
                || !TryReadPointer(Address(first, dialogue.TextBoxOffset), out textBoxAfter)
                || textBoxAfter == IntPtr.Zero
                || !TryReadInt(Address(textBoxAfter, dialogue.VisibleCharactersOffset), out visibleAfter)
                || !TryReadPointer(Address(dialogue.InstanceField.Storage,
                    dialogue.InstanceField.Offset), out second))
            {
                result.Diagnostic = Append(result.Diagnostic, "dialogue-reread-failed");
                return;
            }
            if (second != first || playingAfter != playing || currentLineAfter != currentLine
                || linesAfter != lines || lineCountAfter != lineCount
                || firstLineAfter != firstLine || lineAfter != line
                || textBoxAfter != textBox
                || visibleAfter != visible)
            {
                result.Diagnostic = Append(result.Diagnostic, "dialogue-mutated");
                return;
            }

            result.DialoguePlaying = playing;
            if (playing && currentLine == 0 && visible >= 1 && IsEndingLine(scene, line))
            {
                if (scene == "S_4")
                    result.EndingSignals.Add("ending.bar_dialogue");
                else if (scene == "Subspace_Final")
                    result.EndingSignals.Add("ending.gem_dialogue");
            }
        }

        private bool IsBoundaryMetadataReady(string scene)
        {
            if (scene == "Office_1")
                return IsValidStartLayout(start);
            if (scene == "B_Merdeka")
                return IsValidSurrenderLayout(surrender);
            if (scene == "B_End")
                return IsValidSurrenderLayout(surrender);
            if (scene == "B_Aftermath")
                return IsValidTvStartLayout(tvStart);
            if (IsDialogueScene(scene))
                return IsFullDialogueLayout(dialogue);
            if (scene == "T_Boss" || scene == "Office_3" || scene == "Office_4")
            {
                return IsValidDictionaryLayout(choices)
                    && IsValidStaticField(ending == null ? null : ending.CutscenePlayingField, 1);
            }
            return false;
        }

        private static bool IsDialogueScene(string scene)
        {
            return scene == "S_4" || scene == "Subspace_Final";
        }

        private static bool IsEndingLine(string scene, string line)
        {
            // Unsung Hero: the rule screenshot and sole achievement producer
            // identify S_4 bar/TV, not the reused B_End diner or C_End TV sets.
            if (scene == "S_4")
                return line == "S_L/m20";
            if (scene == "Subspace_Final")
                return line == "Gem/m11";
            return false;
        }

        private DictionaryRead Scan(DictionaryLayout layout, string diagnosticName,
            string marker, ReadResult result)
        {
            return Scan(layout, diagnosticName, marker, false, result);
        }

        private DictionaryRead ScanValue(DictionaryLayout layout, string diagnosticName,
            string marker, ReadResult result)
        {
            return Scan(layout, diagnosticName, marker, true, result);
        }

        private DictionaryRead Scan(DictionaryLayout layout, string diagnosticName,
            string marker, bool readIntegerValue, ReadResult result)
        {
            DictionaryRead unavailable = new DictionaryRead {
                Available = false, Found = false, ValueFound = false, ValueEntry = IntPtr.Zero
            };
            if (layout == null || !IsValidDictionaryLayout(layout))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-unavailable");
                return unavailable;
            }

            IntPtr dictionary;
            if (!TryReadPointer(Address(layout.StaticField.Storage, layout.StaticField.Offset), out dictionary))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                return unavailable;
            }
            // A null optional dictionary is unknown, not a false marker.
            if (dictionary == IntPtr.Zero)
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-unavailable");
                return unavailable;
            }

            int count;
            int version;
            IntPtr entries;
            if (!TryReadInt(Address(dictionary, layout.CountOffset), out count) ||
                !TryReadInt(Address(dictionary, layout.VersionOffset), out version) ||
                !TryReadPointer(Address(dictionary, layout.EntriesOffset), out entries))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                return unavailable;
            }
            if (count < 0 || count > MaxDictionaryCount)
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-unavailable");
                return unavailable;
            }

            int capacity = 0;
            if (entries != IntPtr.Zero)
            {
                if (!TryReadInt(Address(entries, ArrayLengthOffset), out capacity) ||
                    capacity < 0 || capacity > MaxDictionaryCapacity || count > capacity)
                {
                    result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-unavailable");
                    return unavailable;
                }
            }
            else if (count != 0)
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-unavailable");
                return unavailable;
            }

            bool found = false;
            bool valueFound = false;
            int foundValue = 0;
            IntPtr valueEntry = IntPtr.Zero;
            for (int i = 0; i < count; i++)
            {
                IntPtr entry = Address(Address(entries, ArrayDataOffset), checked(i * layout.EntryStride));
                int hashCode;
                if (!TryReadInt(Address(entry, layout.HashCodeOffset), out hashCode))
                {
                    result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                    return unavailable;
                }
                if (hashCode < 0)
                    continue; // deleted/free slot

                int next;
                if (!TryReadInt(Address(entry, layout.NextOffset), out next))
                {
                    result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                    return unavailable;
                }
                // Do not interpret or log next. The metadata-derived read is only
                // used to ensure the entry is readable; chain semantics are runtime
                // implementation details and are not needed for marker membership.

                IntPtr key;
                if (!TryReadPointer(Address(entry, layout.KeyOffset), out key) || key == IntPtr.Zero)
                {
                    result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                    return unavailable;
                }

                int keyLength;
                if (!TryReadInt(Address(key, StringHeaderLengthOffset), out keyLength) ||
                    keyLength < 0 || keyLength > MaxKeyLength)
                {
                    result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-unavailable");
                    return unavailable;
                }

                string value;
                if (!TryReadString(key, out value) || value == null ||
                    value.Length > MaxKeyLength || value.Length != keyLength)
                {
                    result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                    return unavailable;
                }
                if (value == marker)
                {
                    found = true;
                    if (readIntegerValue)
                    {
                        int integerValue;
                        if (!TryReadInt(Address(entry, layout.ValueOffset), out integerValue))
                        {
                            result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                            return unavailable;
                        }
                        if (valueFound && foundValue != integerValue)
                        {
                            result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-mutated");
                            return unavailable;
                        }
                        valueFound = true;
                        foundValue = integerValue;
                        valueEntry = entry;
                    }
                }
            }

            IntPtr dictionaryAfter;
            int versionAfter;
            int countAfter;
            IntPtr entriesAfter;
            if (!TryReadPointer(Address(layout.StaticField.Storage, layout.StaticField.Offset), out dictionaryAfter) ||
                !TryReadInt(Address(dictionary, layout.VersionOffset), out versionAfter) ||
                !TryReadPointer(Address(dictionary, layout.EntriesOffset), out entriesAfter) ||
                !TryReadInt(Address(dictionary, layout.CountOffset), out countAfter))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-read-failed");
                return unavailable;
            }
            if (dictionaryAfter != dictionary || versionAfter != version
                || countAfter != count || entriesAfter != entries)
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-mutated");
                return unavailable;
            }

            int valueAfter;
            if (readIntegerValue && valueFound
                && (!TryReadInt(Address(valueEntry, layout.ValueOffset), out valueAfter)
                    || valueAfter != foundValue))
            {
                result.Diagnostic = Append(result.Diagnostic, diagnosticName + "-mutated");
                return unavailable;
            }

            return new DictionaryRead {
                Available = true,
                Found = found,
                ValueFound = valueFound,
                Value = foundValue,
                ValueEntry = valueEntry
            };
        }

        private static bool IsValidStaticOffset(int offset, int alignment)
        {
            return alignment > 0 && offset >= 0 && offset <= MaxStaticFieldOffset
                && offset % alignment == 0;
        }

        private static bool IsValidStaticField(StaticField field, int alignment)
        {
            return field != null && field.Storage != IntPtr.Zero
                && IsValidStaticOffset(field.Offset, alignment);
        }

        private static bool IsValidDialogueLayout(DialogueLayout layout)
        {
            return layout != null
                && IsValidStaticField(layout.InstanceField, PointerSize)
                && IsValidInstanceOffset(layout.PlayingOffset, 1);
        }

        private static bool IsValidTvStartLayout(TvStartLayout layout)
        {
            return layout != null
                && IsValidStaticField(layout.InstanceStaticAddress, PointerSize)
                && IsValidMetadataPointer(layout.DialogueClassAddress)
                && IsValidInstanceOffset(layout.LinesOffset, PointerSize)
                && IsValidInstanceOffset(layout.CurrentLineOffset, 4)
                && IsValidInstanceOffset(layout.PlayingOffset, 1);
        }

        private static bool IsValidSurrenderLayout(SurrenderLayout layout)
        {
            return layout != null
                && IsValidStaticField(layout.InstanceField, PointerSize)
                && IsValidMetadataPointer(layout.DialogueClassAddress)
                && IsValidMetadataPointer(layout.ButtonChooseClassAddress)
                && IsValidInstanceOffset(layout.DialogueLinesOffset, PointerSize)
                && IsValidInstanceOffset(layout.CurrentLineOffset, 4)
                && IsValidInstanceOffset(layout.LeftStringOffset, PointerSize)
                && IsValidInstanceOffset(layout.RightStringOffset, PointerSize)
                && IsValidInstanceOffset(layout.PlayingOffset, 1)
                && IsValidInstanceOffset(layout.TextingOffset, 1)
                && IsValidInstanceOffset(layout.ButtonChooseOffset, PointerSize)
                && IsValidInstanceOffset(layout.ButtonChoiceOffset, 4);
        }

        private static bool IsFullDialogueLayout(DialogueLayout layout)
        {
            return IsValidDialogueLayout(layout)
                && IsValidInstanceOffset(layout.LinesOffset, PointerSize)
                && IsValidInstanceOffset(layout.CurrentLineOffset, 4)
                && IsValidInstanceOffset(layout.TextBoxOffset, PointerSize)
                && IsValidQualifiedTmpVisibleCharactersOffset(layout.VisibleCharactersOffset);
        }

        private static bool IsValidStartLayout(StartLayout layout)
        {
            return layout != null
                && IsValidStaticField(layout.CutscenePlayingField, 1)
                && IsValidStaticField(layout.InstanceField, PointerSize)
                && IsValidInstanceOffset(layout.PlayerOffset, PointerSize)
                && IsValidInstanceOffset(layout.CanMoveOffset, 1);
        }

        private static bool IsValidEndingLayout(EndingLayout layout)
        {
            return layout != null
                && (IsValidStaticField(layout.CutscenePlayingField, 1)
                    || IsValidStaticField(layout.DeadStateField, 4));
        }

        private static bool IsValidTheodoreLayout(TheodoreLayout layout)
        {
            if (layout == null || !IsValidStaticField(layout.SelectablesField, PointerSize)
                || !IsValidStaticField(layout.SelectableCountField, 4)
                || !IsValidMetadataPointer(layout.ImageAddress)
                || !IsValidMetadataPointer(layout.SelectableClassAddress)
                || !IsValidMetadataPointer(layout.SliderClassAddress)
                || !IsValidMetadataPointer(layout.NavigationClassAddress)
                || !IsValidMetadataPointer(layout.ObjectClassAddress)
                || !IsValidMetadataPointer(layout.RectTransformClassAddress)
                || !IsValidMetadataPointer(layout.GraphicClassAddress))
                return false;
            if (!IsValidInstanceOffset(layout.NavigationOffset, 4)
                || layout.NavigationModeOffset != TheodoreNavigationModeOffset
                || !IsValidValueFieldOffset(layout.NavigationModeOffset, 4)
                || !IsValidInstanceOffset(layout.TargetGraphicOffset, PointerSize)
                || !IsValidInstanceOffset(layout.CachedPtrOffset, PointerSize)
                || !IsValidInstanceOffset(layout.FillRectOffset, PointerSize)
                || !IsValidInstanceOffset(layout.HandleRectOffset, PointerSize)
                || !IsValidInstanceOffset(layout.DirectionOffset, 4)
                || !IsValidInstanceOffset(layout.MinValueOffset, 4)
                || !IsValidInstanceOffset(layout.MaxValueOffset, 4)
                || !IsValidInstanceOffset(layout.WholeNumbersOffset, 1)
                || !IsValidInstanceOffset(layout.ValueOffset, 4))
                return false;
            return layout.CurrentIndexOffset == -1
                || IsValidInstanceOffset(layout.CurrentIndexOffset, 4);
        }

        private static bool IsValidMetadataPointer(IntPtr value)
        {
            return value != IntPtr.Zero && value.ToInt64() > 0;
        }

        private static bool IsValidValueFieldOffset(int offset, int alignment)
        {
            return alignment > 0 && offset >= 0 && offset <= MaxInstanceFieldOffset
                && offset % alignment == 0;
        }

        private static bool IsValidInstanceOffset(int offset, int alignment)
        {
            return alignment > 0 && offset >= PointerSize * 2
                && offset <= MaxInstanceFieldOffset && offset % alignment == 0;
        }

        private static bool IsValidQualifiedTmpVisibleCharactersOffset(int offset)
        {
            return offset == QualifiedTmpVisibleCharactersOffset;
        }

        private static bool IsValidDictionaryLayout(DictionaryLayout layout)
        {
            if (layout == null || !IsValidStaticField(layout.StaticField, PointerSize))
                return false;
            if (layout.EntriesOffset != DictionaryEntriesOffset
                || layout.CountOffset != DictionaryCountOffset
                || layout.VersionOffset != DictionaryVersionOffset)
                return false;
            if (layout.HashCodeOffset != 0 || layout.NextOffset != 4
                || layout.KeyOffset != 8 || layout.ValueOffset != 16
                || layout.EntryStride != 24)
                return false;
            return true;
        }

        private bool TryReadInt(IntPtr address, out int value)
        {
            value = 0;
            try
            {
                return memory.TryReadInt(address, out value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryReadBool(IntPtr address, out bool value)
        {
            value = false;
            try
            {
                return memory.TryReadBool(address, out value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryReadPointer(IntPtr address, out IntPtr value)
        {
            value = IntPtr.Zero;
            try
            {
                return memory.TryReadPointer(address, out value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryReadString(IntPtr address, out string value)
        {
            value = null;
            try
            {
                return memory.TryReadString(address, out value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IntPtr Address(IntPtr address, int offset)
        {
            return new IntPtr(checked(address.ToInt64() + offset));
        }

        private static string Append(string first, string second)
        {
            if (String.IsNullOrEmpty(first))
                return second;
            if (String.IsNullOrEmpty(second))
                return first;
            return first + ";" + second;
        }

        private void AddConfigurationDiagnostic(string diagnostic)
        {
            configurationDiagnostic = Append(configurationDiagnostic, diagnostic);
        }

        private static object GetMember(object value, string name)
        {
            if (value == null)
                return null;
            Type type = value.GetType();
            PropertyInfo property = type.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
                return property.GetValue(value, null);
            FieldInfo field = type.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(value);
        }

        private static object FindField(object monoClass, string normalizedName)
        {
            IEnumerable fields = monoClass as IEnumerable;
            if (fields == null)
                return null;
            foreach (object field in fields)
            {
                object name = GetMember(field, "Name");
                if (name is string && (string)name == normalizedName)
                    return field;
            }
            return null;
        }

        private static int GetOffset(object field)
        {
            object value = GetMember(field, "Offset");
            if (value == null)
                throw new InvalidOperationException("field-offset-unavailable");
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static IntPtr GetStorage(object monoClass, string className)
        {
            object value = GetMember(monoClass, "Static");
            IntPtr result;
            if (!TryConvertPointer(value, out result) || result == IntPtr.Zero)
                throw new InvalidOperationException(className + "-static-storage-unavailable");
            return result;
        }

        private static StaticField ResolveStorage(object monoClass, string className)
        {
            return new StaticField { Storage = GetStorage(monoClass, className) };
        }

        private static bool TryConvertPointer(object value, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (value == null)
                return false;
            if (value is IntPtr)
            {
                result = (IntPtr)value;
                return true;
            }
            try
            {
                result = new IntPtr(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static StartLayout ResolveStart(object sceneClass)
        {
            if (sceneClass == null)
                return null;
            StaticField storage = ResolveStorage(sceneClass, "MySceneManager");
            object cutsceneField = FindField(sceneClass, "CutscenePlaying");
            object instanceField = FindField(sceneClass, "instance");
            object playerField = FindField(sceneClass, "_player");
            if (cutsceneField == null || instanceField == null || playerField == null
                || !HasStaticState(cutsceneField, true)
                || !HasStaticState(instanceField, true)
                || !HasStaticState(playerField, false)
                || !IsBooleanType(GetMember(cutsceneField, "Type")))
                return null;

            object instanceClass = GetMember(GetMember(instanceField, "Type"), "Class");
            object playerClass = GetMember(GetMember(playerField, "Type"), "Class");
            if (ClassName(instanceClass) != "MySceneManager"
                || ClassName(playerClass) != "Player")
                return null;

            // CanMove is a computed property, not a Mono field. Office_1's
            // StartTheGame setter writes this private Boolean backing flag.
            // Keep the cutscene/fresh-run guards; do not execute a game getter.
            object canMoveField = FindField(playerClass, "_canMove");
            if (canMoveField == null || !HasStaticState(canMoveField, false)
                || !IsBooleanType(GetMember(canMoveField, "Type")))
                return null;

            int cutsceneOffset = GetOffset(cutsceneField);
            int instanceOffset = GetOffset(instanceField);
            int playerOffset = GetOffset(playerField);
            int canMoveOffset = GetOffset(canMoveField);
            StartLayout layout = new StartLayout {
                CutscenePlayingField = new StaticField {
                    Storage = storage.Storage, Offset = cutsceneOffset
                },
                InstanceField = new StaticField {
                    Storage = storage.Storage, Offset = instanceOffset
                },
                PlayerOffset = playerOffset,
                CanMoveOffset = canMoveOffset
            };
            return IsValidStartLayout(layout) ? layout : null;
        }

        private static EndingLayout ResolveEnding(object sceneClass)
        {
            if (sceneClass == null)
                return null;
            StaticField storage = ResolveStorage(sceneClass, "MySceneManager");
            EndingLayout layout = new EndingLayout {
                CutscenePlayingField = ResolveOptionalStaticField(sceneClass, storage,
                    "CutscenePlaying", true, 1),
                DeadStateField = ResolveOptionalStaticField(sceneClass, storage,
                    "DeadState", false, 4)
            };
            return IsValidEndingLayout(layout) ? layout : null;
        }

        private static TheodoreLayout ResolveTheodore(dynamic mono,
            Action<string> reportStage)
        {
            string stage = "image";
            try
            {
                if (mono == null)
                    return TheodoreFailure(reportStage, stage);
                dynamic uiImage = mono.GetImage("UnityEngine.UI");
                IntPtr imageAddress;
                if (!TryConvertPointer(GetMember(uiImage, "Address"), out imageAddress)
                    || !IsValidMetadataPointer(imageAddress))
                    return TheodoreFailure(reportStage, stage);

                stage = "classes";
                object selectableClass = ResolveMonoClass(mono, "UnityEngine.UI",
                    "UnityEngine.UI.Selectable");
                object sliderClass = ResolveMonoClass(mono, "UnityEngine.UI",
                    "UnityEngine.UI.Slider");
                object navigationClass = ResolveMonoClass(mono, "UnityEngine.UI",
                    "UnityEngine.UI.Navigation");
                object objectClass = ResolveMonoClass(mono, "UnityEngine.CoreModule",
                    "UnityEngine.Object");
                object rectTransformClass = ResolveMonoClass(mono,
                    "UnityEngine.CoreModule", "UnityEngine.RectTransform");
                object graphicClass = ResolveMonoClass(mono,
                    "UnityEngine.UI", "UnityEngine.UI.Graphic");
                if (!IsQualifiedClass(selectableClass, "Selectable", "UnityEngine.UI")
                    || !IsQualifiedClass(sliderClass, "Slider", "UnityEngine.UI")
                    || !IsQualifiedClass(navigationClass, "Navigation", "UnityEngine.UI")
                    || !IsQualifiedClass(objectClass, "Object", "UnityEngine")
                    || !IsQualifiedClass(rectTransformClass, "RectTransform", "UnityEngine")
                    || !IsQualifiedClass(graphicClass, "Graphic", "UnityEngine.UI"))
                    return TheodoreFailure(reportStage, stage);

                IntPtr selectableAddress = GetClassAddress(selectableClass);
                IntPtr sliderAddress = GetClassAddress(sliderClass);
                IntPtr navigationAddress = GetClassAddress(navigationClass);
                IntPtr objectAddress = GetClassAddress(objectClass);
                IntPtr rectTransformAddress = GetClassAddress(rectTransformClass);
                IntPtr graphicAddress = GetClassAddress(graphicClass);

                // Use the pinned helper's metadata-only class-parent reader.
                // No Unity method/getter or guessed MonoClass offset is invoked.
                // It is internal in this helper version, so bind its exact
                // signature once and keep the traversal bounded below.
                stage = "selectable-ancestry";
                object metadataManager = (object)mono;
                MethodInfo classParent = metadataManager.GetType().GetMethod("ClassParent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(IntPtr) }, null);
                if (classParent == null || classParent.ReturnType != typeof(IntPtr))
                    return TheodoreFailure(reportStage, stage);
                Func<IntPtr, IntPtr> readParent = delegate(IntPtr address) {
                    return (IntPtr)classParent.Invoke(metadataManager, new object[] { address });
                };

                stage = "registry-fields";
                StaticField registryStorage = ResolveStorage(selectableClass,
                    "UnityEngine.UI.Selectable");
                object selectablesField = FindField(selectableClass, "s_Selectables");
                object countField = FindField(selectableClass, "s_SelectableCount");
                if (selectablesField == null || countField == null
                    || !HasStaticState(selectablesField, true)
                    || !HasStaticState(countField, true)
                    || !IsExactSelectableArrayType(GetMember(selectablesField, "Type"),
                        selectableAddress)
                    || !IsInt32Type(GetMember(countField, "Type")))
                    return TheodoreFailure(reportStage, stage);
                int selectablesOffset = GetOffset(selectablesField);
                int countOffset = GetOffset(countField);
                TheodoreLayout layout = new TheodoreLayout {
                    SelectablesField = new StaticField {
                        Storage = registryStorage.Storage, Offset = selectablesOffset
                    },
                    SelectableCountField = new StaticField {
                        Storage = registryStorage.Storage, Offset = countOffset
                    },
                    ImageAddress = imageAddress,
                    ClassParent = readParent,
                    SelectableClassAddress = selectableAddress,
                    SliderClassAddress = sliderAddress,
                    NavigationClassAddress = navigationAddress,
                    ObjectClassAddress = objectAddress,
                    RectTransformClassAddress = rectTransformAddress,
                    GraphicClassAddress = graphicAddress,
                    CurrentIndexOffset = -1,
                    NavigationModeOffset = -1
                };
                if (!IsValidStaticField(layout.SelectablesField, PointerSize)
                    || !IsValidStaticField(layout.SelectableCountField, 4))
                    return TheodoreFailure(reportStage, stage);

                stage = "selectable-fields";
                object navigationField = FindField(selectableClass, "m_Navigation");
                object targetGraphicField = FindField(selectableClass, "m_TargetGraphic");
                object currentIndexField = FindField(selectableClass, "m_CurrentIndex");
                if (!IsValueTypeOfClass(GetMember(navigationField, "Type"),
                        navigationAddress)
                    || !IsClassReferenceType(GetMember(targetGraphicField, "Type"),
                        graphicAddress, "Graphic", "UnityEngine.UI")
                    || !HasStaticState(navigationField, false)
                    || !HasStaticState(targetGraphicField, false))
                    return TheodoreFailure(reportStage, stage);
                layout.NavigationOffset = GetOffset(navigationField);
                layout.TargetGraphicOffset = GetOffset(targetGraphicField);
                if (currentIndexField != null)
                {
                    if (!HasStaticState(currentIndexField, false)
                        || !IsInt32Type(GetMember(currentIndexField, "Type")))
                        return TheodoreFailure(reportStage, stage);
                    layout.CurrentIndexOffset = GetOffset(currentIndexField);
                }

                stage = "object-field";
                object cachedPtrField = FindField(objectClass, "m_CachedPtr");
                if (cachedPtrField == null || !HasStaticState(cachedPtrField, false)
                    || !IsIntPtrType(GetMember(cachedPtrField, "Type")))
                    return TheodoreFailure(reportStage, stage);
                layout.CachedPtrOffset = GetOffset(cachedPtrField);

                stage = "slider-fields";
                object fillRectField = FindField(sliderClass, "m_FillRect");
                object handleRectField = FindField(sliderClass, "m_HandleRect");
                object directionField = FindField(sliderClass, "m_Direction");
                object minValueField = FindField(sliderClass, "m_MinValue");
                object maxValueField = FindField(sliderClass, "m_MaxValue");
                object wholeNumbersField = FindField(sliderClass, "m_WholeNumbers");
                object valueField = FindField(sliderClass, "m_Value");
                if (!IsClassReferenceType(GetMember(fillRectField, "Type"),
                        rectTransformAddress, "RectTransform", "UnityEngine")
                    || !IsClassReferenceType(GetMember(handleRectField, "Type"),
                        rectTransformAddress, "RectTransform", "UnityEngine")
                    || !IsEnumType(GetMember(directionField, "Type"), "Direction")
                    || !IsFloatType(GetMember(minValueField, "Type"))
                    || !IsFloatType(GetMember(maxValueField, "Type"))
                    || !IsBooleanType(GetMember(wholeNumbersField, "Type"))
                    || !IsFloatType(GetMember(valueField, "Type"))
                    || !HasStaticState(fillRectField, false)
                    || !HasStaticState(handleRectField, false)
                    || !HasStaticState(directionField, false)
                    || !HasStaticState(minValueField, false)
                    || !HasStaticState(maxValueField, false)
                    || !HasStaticState(wholeNumbersField, false)
                    || !HasStaticState(valueField, false))
                    return TheodoreFailure(reportStage, stage);
                layout.FillRectOffset = GetOffset(fillRectField);
                layout.HandleRectOffset = GetOffset(handleRectField);
                layout.DirectionOffset = GetOffset(directionField);
                layout.MinValueOffset = GetOffset(minValueField);
                layout.MaxValueOffset = GetOffset(maxValueField);
                layout.WholeNumbersOffset = GetOffset(wholeNumbersField);
                layout.ValueOffset = GetOffset(valueField);

                stage = "navigation-field";
                object navigationModeField = FindField(navigationClass, "m_Mode");
                if (!IsEnumType(GetMember(navigationModeField, "Type"), "Mode")
                    || !HasStaticState(navigationModeField, false))
                    return TheodoreFailure(reportStage, stage);
                layout.NavigationModeOffset = GetOffset(navigationModeField);
                if (layout.NavigationModeOffset != TheodoreNavigationModeOffset
                    || !IsValidTheodoreLayout(layout))
                    return TheodoreFailure(reportStage, stage);
                return layout;
            }
            catch (Exception)
            {
                return TheodoreFailure(reportStage, stage);
            }
        }

        private static TheodoreLayout TheodoreFailure(Action<string> reportStage,
            string stage)
        {
            if (reportStage != null)
                reportStage("theodore-metadata-stage-" + stage);
            return null;
        }

        private static StaticField ResolveOptionalStaticField(object monoClass,
            StaticField storage, string name, bool booleanType, int alignment)
        {
            object field = FindField(monoClass, name);
            if (field == null || !HasStaticState(field, true))
                return null;
            object type = GetMember(field, "Type");
            if (booleanType ? !IsBooleanType(type) : !IsInt32Type(type))
                return null;
            int offset;
            try
            {
                offset = GetOffset(field);
            }
            catch (Exception)
            {
                return null;
            }
            return IsValidStaticOffset(offset, alignment)
                ? new StaticField { Storage = storage.Storage, Offset = offset } : null;
        }

        private static SurrenderLayout ResolveSurrender(dynamic mono,
            object dialogueClass, object buttonChooseClass,
            Action<string> reportStage)
        {
            string stage = "classes";
            try
            {
                if (dialogueClass == null || buttonChooseClass == null
                    || ClassName(dialogueClass) != "DialogueManager"
                    || ClassName(buttonChooseClass) != "Button_Choose")
                    return SurrenderFailure(reportStage, stage);
                IntPtr dialogueAddress = GetClassAddress(dialogueClass);
                IntPtr buttonAddress = GetClassAddress(buttonChooseClass);
                if (!IsValidMetadataPointer(dialogueAddress)
                    || !IsValidMetadataPointer(buttonAddress))
                    return SurrenderFailure(reportStage, stage);

                stage = "storage";
                StaticField storage = ResolveStorage(dialogueClass,
                    "DialogueManager");
                stage = "dialogue-fields";
                object instanceField = FindField(dialogueClass, "Instance");
                object linesField = FindField(dialogueClass, "DILines");
                object currentLineField = FindField(dialogueClass,
                    "CurrentLineIndex");
                object leftField = FindField(dialogueClass, "LeftString");
                object rightField = FindField(dialogueClass, "RightString");
                object playingField = FindField(dialogueClass, "Playing");
                object textingField = FindField(dialogueClass, "Texting");
                object chooserField = FindField(dialogueClass, "BChoose");
                if (instanceField == null || linesField == null
                    || currentLineField == null || leftField == null
                    || rightField == null || playingField == null
                    || textingField == null || chooserField == null
                    || !HasStaticState(instanceField, true)
                    || !HasStaticState(linesField, false)
                    || !HasStaticState(currentLineField, false)
                    || !HasStaticState(leftField, false)
                    || !HasStaticState(rightField, false)
                    || !HasStaticState(playingField, false)
                    || !HasStaticState(textingField, false)
                    || !HasStaticState(chooserField, false))
                    return SurrenderFailure(reportStage, stage);
                if (!IsExactTypeClass(GetMember(instanceField, "Type"),
                        dialogueAddress, "DialogueManager", null)
                    || !IsStringArrayType(GetMember(linesField, "Type"))
                    || !IsInt32Type(GetMember(currentLineField, "Type"))
                    || !IsStringType(GetMember(leftField, "Type"))
                    || !IsStringType(GetMember(rightField, "Type"))
                    || !IsBooleanType(GetMember(playingField, "Type"))
                    || !IsBooleanType(GetMember(textingField, "Type")))
                    return SurrenderFailure(reportStage, "dialogue-types");
                if (!IsClassReferenceAt(GetMember(chooserField, "Type"),
                    buttonChooseClass, buttonAddress))
                    return SurrenderFailure(reportStage, "chooser-type");

                stage = "chooser-fields";
                object choiceField = FindField(buttonChooseClass, "ButtonChoice");
                if (choiceField == null || !HasStaticState(choiceField, false)
                    || !IsInt32Type(GetMember(choiceField, "Type")))
                    return SurrenderFailure(reportStage, stage);

                SurrenderLayout layout = new SurrenderLayout {
                    InstanceField = new StaticField {
                        Storage = storage.Storage,
                        Offset = GetOffset(instanceField)
                    },
                    DialogueClassAddress = dialogueAddress,
                    ButtonChooseClassAddress = buttonAddress,
                    DialogueLinesOffset = GetOffset(linesField),
                    CurrentLineOffset = GetOffset(currentLineField),
                    LeftStringOffset = GetOffset(leftField),
                    RightStringOffset = GetOffset(rightField),
                    PlayingOffset = GetOffset(playingField),
                    TextingOffset = GetOffset(textingField),
                    ButtonChooseOffset = GetOffset(chooserField),
                    ButtonChoiceOffset = GetOffset(choiceField)
                };
                if (!IsValidSurrenderLayout(layout))
                    return SurrenderFailure(reportStage, "offsets");
                return layout;
            }
            catch (Exception)
            {
                return SurrenderFailure(reportStage, stage);
            }
        }

        private static bool IsClassReferenceAt(object monoType,
            object expectedClass, IntPtr expectedAddress)
        {
            if (monoType == null || ElementTypeName(monoType) != "Class")
                return false;
            object actualClass = GetMember(monoType, "Class");
            return actualClass != null && SameMonoClass(actualClass, expectedClass)
                && GetClassAddress(actualClass) == expectedAddress;
        }

        private static SurrenderLayout SurrenderFailure(Action<string> reportStage,
            string stage)
        {
            if (reportStage != null)
                reportStage("surrender-metadata-stage-" + stage);
            return null;
        }

        private static DialogueLayout ResolveDialogue(dynamic mono, object dialogueClass,
            Action<string> reportStage)
        {
            if (dialogueClass == null)
                return DialogueFailure(null, reportStage, "dialogue-class");
            StaticField storage = ResolveStorage(dialogueClass, "DialogueManager");
            object instanceField = FindField(dialogueClass, "Instance");
            object playingField = FindField(dialogueClass, "Playing");
            if (instanceField == null || playingField == null
                || !HasStaticState(instanceField, true) || !HasStaticState(playingField, false))
                return DialogueFailure(null, reportStage, "required-fields");

            object instanceType = GetMember(instanceField, "Type");
            object instanceClass = GetMember(instanceType, "Class");
            if (ClassName(instanceClass) != "DialogueManager"
                || !IsBooleanType(GetMember(playingField, "Type")))
                return DialogueFailure(null, reportStage, "required-types");

            DialogueLayout layout = new DialogueLayout {
                InstanceField = new StaticField {
                    Storage = storage.Storage,
                    Offset = GetOffset(instanceField)
                },
                PlayingOffset = GetOffset(playingField),
                LinesOffset = -1,
                CurrentLineOffset = -1,
                TextBoxOffset = -1,
                VisibleCharactersOffset = -1
            };
            if (!IsValidDialogueLayout(layout))
                return DialogueFailure(null, reportStage, "base-layout");

            object linesField = FindField(dialogueClass, "DILines");
            object currentLineField = FindField(dialogueClass, "CurrentLineIndex");
            object textBoxField = FindField(dialogueClass, "TextBox");
            if (textBoxField == null)
                textBoxField = FindField(dialogueClass, "TextUI");
            if (linesField == null || currentLineField == null || textBoxField == null
                || !HasStaticState(linesField, false)
                || !HasStaticState(currentLineField, false)
                || !HasStaticState(textBoxField, false))
                return DialogueFailure(layout, reportStage, "payload-fields");
            if (!IsStringArrayType(GetMember(linesField, "Type"))
                || !IsInt32Type(GetMember(currentLineField, "Type")))
                return DialogueFailure(layout, reportStage, "payload-types");

            object textBoxType = GetMember(textBoxField, "Type");
            object textBoxClass = GetMember(textBoxType, "Class");
            string textBoxName = ClassName(textBoxClass);
            string textBoxNamespace = ClassNamespace(textBoxClass);
            if (textBoxClass == null || textBoxName != "TextMeshProUGUI"
                || textBoxNamespace != "TMPro")
                return DialogueFailure(layout, reportStage, "textbox-class");

            // MonoClass exposes no parent/declaring-class accessor.  The pinned
            // helper's supported route is the Unity.TextMeshPro image followed by
            // its namespace-qualified class lookup.  The pinned managed assembly
            // identifies TMPro.TextMeshProUGUI : TMPro.TMP_Text; match the
            // dialogue field's class to that exact image-owned class, then resolve
            // the inherited field from its exact TMP_Text owner.
            object textMeshProUiClass = ResolveMonoClass(mono, "Unity.TextMeshPro",
                "TMPro.TextMeshProUGUI");
            if (textMeshProUiClass == null || ClassName(textMeshProUiClass) != "TextMeshProUGUI"
                || ClassNamespace(textMeshProUiClass) != "TMPro")
                return DialogueFailure(layout, reportStage, "textbox-class-image");
            if (!SameMonoClass(textBoxClass, textMeshProUiClass))
                return DialogueFailure(layout, reportStage, "textbox-class-identity");

            object tmpTextClass = ResolveMonoClass(mono, "Unity.TextMeshPro", "TMPro.TMP_Text");
            if (tmpTextClass == null || ClassName(tmpTextClass) != "TMP_Text"
                || ClassNamespace(tmpTextClass) != "TMPro")
                return DialogueFailure(layout, reportStage, "tmp-text-class");

            object visibleField = FindField(tmpTextClass, "m_maxVisibleCharacters");
            if (visibleField == null || !HasStaticState(visibleField, false))
                return DialogueFailure(layout, reportStage, "tmp-visible-field");
            if (!IsInt32Type(GetMember(visibleField, "Type")))
                return DialogueFailure(layout, reportStage, "tmp-visible-type");

            int linesOffset = GetOffset(linesField);
            int currentLineOffset = GetOffset(currentLineField);
            int textBoxOffset = GetOffset(textBoxField);
            int visibleOffset = GetOffset(visibleField);
            if (!IsValidInstanceOffset(linesOffset, PointerSize)
                || !IsValidInstanceOffset(currentLineOffset, 4)
                || !IsValidInstanceOffset(textBoxOffset, PointerSize))
                return DialogueFailure(layout, reportStage, "payload-offset");
            if (!IsValidQualifiedTmpVisibleCharactersOffset(visibleOffset))
                return DialogueFailure(layout, reportStage, "tmp-visible-offset");

            layout.LinesOffset = linesOffset;
            layout.CurrentLineOffset = currentLineOffset;
            layout.TextBoxOffset = textBoxOffset;
            layout.VisibleCharactersOffset = visibleOffset;
            return layout;
        }

        private static DialogueLayout DialogueFailure(DialogueLayout layout,
            Action<string> reportStage, string stage)
        {
            if (reportStage != null)
                reportStage("dialogue-metadata-stage-" + stage);
            return layout;
        }

        private static TvStartLayout TryResolveTvStart(dynamic mono,
            object dialogueClass, Action<string> reportStage)
        {
            string stage = "class";
            try
            {
                if (dialogueClass == null
                    || ClassName(dialogueClass) != "DialogueManager")
                    return TvStartFailure(reportStage, stage);

                IntPtr dialogueAddress = GetClassAddress(dialogueClass);
                if (!IsValidMetadataPointer(dialogueAddress))
                    return TvStartFailure(reportStage, stage);

                stage = "storage";
                StaticField storage = ResolveStorage(dialogueClass,
                    "DialogueManager");
                stage = "fields";
                object instanceField = FindField(dialogueClass, "Instance");
                object linesField = FindField(dialogueClass, "DILines");
                object currentLineField = FindField(dialogueClass,
                    "CurrentLineIndex");
                object playingField = FindField(dialogueClass, "Playing");
                if (instanceField == null || linesField == null
                    || currentLineField == null || playingField == null
                    || !HasStaticState(instanceField, true)
                    || !HasStaticState(linesField, false)
                    || !HasStaticState(currentLineField, false)
                    || !HasStaticState(playingField, false))
                    return TvStartFailure(reportStage, stage);

                object instanceType = GetMember(instanceField, "Type");
                if (!IsExactTypeClass(instanceType, dialogueAddress,
                        "DialogueManager", ClassNamespace(dialogueClass))
                    || !IsStringArrayType(GetMember(linesField, "Type"))
                    || !IsInt32Type(GetMember(currentLineField, "Type"))
                    || !IsBooleanType(GetMember(playingField, "Type")))
                    return TvStartFailure(reportStage, "types");

                TvStartLayout layout = new TvStartLayout {
                    InstanceStaticAddress = new StaticField {
                        Storage = storage.Storage,
                        Offset = GetOffset(instanceField)
                    },
                    DialogueClassAddress = dialogueAddress,
                    LinesOffset = GetOffset(linesField),
                    CurrentLineOffset = GetOffset(currentLineField),
                    PlayingOffset = GetOffset(playingField),
                    TestDirectClassIdentity = false
                };
                if (!IsValidTvStartLayout(layout))
                    return TvStartFailure(reportStage, "offsets");
                return layout;
            }
            catch (Exception)
            {
                return TvStartFailure(reportStage, stage);
            }
        }

        private static TvStartLayout TvStartFailure(Action<string> reportStage,
            string stage)
        {
            if (reportStage != null)
                reportStage("tv-start-metadata-stage-" + stage);
            return null;
        }

        private static object ResolveMonoClass(dynamic mono, string imageName, string className)
        {
            if (mono == null)
                return null;
            try
            {
                // UnityMemManager.GetImage(string) and
                // UnityMemManager.GetClass(MonoImage, string) are the pinned
                // asl-help API.  Do not use an assumed MonoClass parent member or
                // an assumed indexer arity.
                dynamic image = mono.GetImage(imageName);
                return image == null ? null : mono.GetClass(image, className);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsQualifiedClass(object monoClass, string name,
            string nameSpace)
        {
            return monoClass != null && ClassName(monoClass) == name
                && ClassNamespace(monoClass) == nameSpace
                && IsValidMetadataPointer(GetClassAddress(monoClass));
        }

        private static IntPtr GetClassAddress(object monoClass)
        {
            IntPtr address;
            return TryConvertPointer(GetMember(monoClass, "Address"), out address)
                ? address : IntPtr.Zero;
        }

        private static bool IsExactSelectableArrayType(object monoType,
            IntPtr selectableClassAddress)
        {
            if (!IsSzArrayType(monoType))
                return false;
            IntPtr elementClass;
            return TryConvertPointer(GetMember(monoType, "Data"), out elementClass)
                && elementClass == selectableClassAddress;
        }

        private static bool IsClassReferenceType(object monoType,
            IntPtr classAddress, string name, string nameSpace)
        {
            return ElementTypeName(monoType) == "Class"
                && IsExactTypeClass(monoType, classAddress, name, nameSpace);
        }

        private static bool IsValueTypeOfClass(object monoType, IntPtr classAddress)
        {
            return IsValueTypeDescriptor(ElementTypeName(monoType))
                && IsExactTypeClass(monoType, classAddress, null, null);
        }

        private static bool IsEnumType(object monoType, string expectedName)
        {
            if (!IsValueTypeDescriptor(ElementTypeName(monoType)))
                return false;
            object monoClass = GetMember(monoType, "Class");
            string nameSpace = ClassNamespace(monoClass);
            return monoClass != null && ClassName(monoClass) == expectedName
                // Mono reports an empty namespace for these nested enum types.
                // Their fields are resolved from the exact pinned Slider and
                // Navigation owners; do not invent a namespace from the CLR name.
                && (nameSpace == "" || nameSpace == "UnityEngine.UI"
                    || nameSpace == "UnityEngine.UI.Navigation"
                    || nameSpace == "UnityEngine.UI.Slider");
        }

        private static bool IsExactTypeClass(object monoType, IntPtr classAddress,
            string expectedName, string expectedNamespace)
        {
            object monoClass = GetMember(monoType, "Class");
            if (monoClass == null || GetClassAddress(monoClass) != classAddress)
                return false;
            return (expectedName == null || ClassName(monoClass) == expectedName)
                && (expectedNamespace == null
                    || ClassNamespace(monoClass) == expectedNamespace);
        }

        private static bool IsValueTypeDescriptor(string descriptor)
        {
            return descriptor == "ValueType" || descriptor == "17";
        }

        private static bool IsFloatType(object monoType)
        {
            string descriptor = ElementTypeName(monoType);
            return descriptor == "R4" || descriptor == "12";
        }

        private static bool IsIntPtrType(object monoType)
        {
            string descriptor = ElementTypeName(monoType);
            return descriptor == "I" || descriptor == "IntPtr"
                || descriptor == "System_IntPtr";
        }

        private static bool HasStaticState(object field, bool expected)
        {
            object value = GetMember(field, "IsStatic");
            return value is bool && (bool)value == expected;
        }

        private DictionaryLayout ResolveDictionary(StaticField storage, object dictionaryField,
            object classPrototype, string diagnosticName)
        {
            string stage = "field";
            try
            {
                if (dictionaryField == null || !HasStaticState(dictionaryField, true))
                    return DictionaryFailure(diagnosticName, stage);

                stage = "generic-type";
                object dictionaryType = GetMember(dictionaryField, "Type");
                if (!IsGenericInstType(dictionaryType))
                    return DictionaryFailure(diagnosticName, stage);

                stage = "generic-class";
                object dictionaryClass = ResolveGenericInstClass(dictionaryType, classPrototype);
                if (dictionaryClass == null
                    || ClassName(dictionaryClass) != "Dictionary_2"
                    || ClassNamespace(dictionaryClass) != "System.Collections.Generic")
                    return DictionaryFailure(diagnosticName, stage);

                stage = "dictionary-shape";
                object bucketsField = FindField(dictionaryClass, "_buckets");
                object entriesField = FindField(dictionaryClass, "_entries");
                object comparerField = FindField(dictionaryClass, "_comparer");
                object keysField = FindField(dictionaryClass, "_keys");
                object valuesField = FindField(dictionaryClass, "_values");
                object syncRootField = FindField(dictionaryClass, "_syncRoot");
                object countField = FindField(dictionaryClass, "_count");
                object freeListField = FindField(dictionaryClass, "_freeList");
                object freeCountField = FindField(dictionaryClass, "_freeCount");
                object versionField = FindField(dictionaryClass, "_version");
                if (!IsDictionaryField(bucketsField, DictionaryBucketsOffset, false)
                    || !IsDictionaryField(entriesField, DictionaryEntriesOffset, false)
                    || !IsDictionaryField(comparerField, DictionaryComparerOffset, false)
                    || !IsDictionaryField(keysField, DictionaryKeysOffset, false)
                    || !IsDictionaryField(valuesField, DictionaryValuesOffset, false)
                    || !IsDictionaryField(syncRootField, DictionarySyncRootOffset, false)
                    || !IsDictionaryField(countField, DictionaryCountOffset, false)
                    || !IsDictionaryField(freeListField, DictionaryFreeListOffset, false)
                    || !IsDictionaryField(freeCountField, DictionaryFreeCountOffset, false)
                    || !IsDictionaryField(versionField, DictionaryVersionOffset, false))
                    return DictionaryFailure(diagnosticName, stage);

                stage = "array-types";
                object bucketsClass = ResolveArrayElementClass(GetMember(bucketsField, "Type"),
                    classPrototype, "Int32", "System");
                object entriesType = GetMember(entriesField, "Type");
                // The nested Entry type has an empty metadata namespace; its
                // owning Dictionary is established by this exact _entries field.
                object entryClass = ResolveArrayElementClass(entriesType, classPrototype,
                    "Entry", "");
                if (bucketsClass == null || entryClass == null)
                    return DictionaryFailure(diagnosticName, stage);

                // These are generic collection/comparer references, not T[] arrays.
                // They are not dereferenced by the scan, but their declared kind
                // and object offsets remain part of the qualified layout contract.
                stage = "auxiliary-types";
                if (!IsGenericInstType(GetMember(comparerField, "Type"))
                    || !IsGenericInstType(GetMember(keysField, "Type"))
                    || !IsGenericInstType(GetMember(valuesField, "Type"))
                    || (ElementTypeName(GetMember(syncRootField, "Type")) != "Object"
                        && ElementTypeName(GetMember(syncRootField, "Type")) != "28"))
                    return DictionaryFailure(diagnosticName, stage);

                stage = "scalar-types";
                if (!IsClosedInt32Type(GetMember(countField, "Type"))
                    || !IsClosedInt32Type(GetMember(freeListField, "Type"))
                    || !IsClosedInt32Type(GetMember(freeCountField, "Type"))
                    || !IsClosedInt32Type(GetMember(versionField, "Type")))
                    return DictionaryFailure(diagnosticName, stage);

                stage = "entry-shape";
                object hashField = FindField(entryClass, "hashCode");
                object nextField = FindField(entryClass, "next");
                object keyField = FindField(entryClass, "key");
                object valueField = FindField(entryClass, "value");
                if (!IsDictionaryField(hashField, EntryHashCodeOffset, false)
                    || !IsDictionaryField(nextField, EntryNextOffset, false)
                    || !IsDictionaryField(keyField, EntryKeyOffset, false)
                    || !IsDictionaryField(valueField, EntryValueOffset, false)
                    || !IsClosedInt32Type(GetMember(hashField, "Type"))
                    || !IsClosedInt32Type(GetMember(nextField, "Type"))
                    || !IsClosedStringType(GetMember(keyField, "Type"))
                    || !IsClosedInt32Type(GetMember(valueField, "Type")))
                    return DictionaryFailure(diagnosticName, stage);

                int dictionaryOffset = GetOffset(dictionaryField);
                if (!IsValidStaticOffset(dictionaryOffset, PointerSize))
                    return DictionaryFailure(diagnosticName, stage);

                // asl-help's MonoField.Offset getter already subtracts the
                // struct adjustment for non-static Entry fields.  Use those
                // helper-normalized offsets directly for Entry[] rows.
                DictionaryLayout layout = new DictionaryLayout {
                    StaticField = new StaticField {
                        Storage = storage.Storage, Offset = dictionaryOffset
                    },
                    EntriesOffset = DictionaryEntriesOffset,
                    CountOffset = DictionaryCountOffset,
                    VersionOffset = DictionaryVersionOffset,
                    HashCodeOffset = GetOffset(hashField),
                    NextOffset = GetOffset(nextField),
                    KeyOffset = GetOffset(keyField),
                    ValueOffset = GetOffset(valueField),
                    EntryStride = QualifiedEntryStride
                };
                if (!IsValidDictionaryLayout(layout))
                    return DictionaryFailure(diagnosticName, stage);
                return layout;
            }
            catch (Exception)
            {
                return DictionaryFailure(diagnosticName, stage);
            }
        }

        private DictionaryLayout DictionaryFailure(string diagnosticName, string stage)
        {
            AddConfigurationDiagnostic(diagnosticName + "-metadata-stage-" + stage);
            return null;
        }

        private object ResolveGenericInstClass(object monoType, object classPrototype)
        {
            // Never ask MonoType.Class for GenericInst: the pinned helper follows
            // GenericClass.context, which is class_inst rather than MonoClass on
            // the qualified original Mono runtime.
            object dataValue = GetMember(monoType, "Data");
            IntPtr data;
            if (!TryConvertPointer(dataValue, out data) || data == IntPtr.Zero)
                return null;
            IntPtr cachedClass;
            if (!TryReadPointer(Address(data, GenericClassCachedClassOffset), out cachedClass)
                || cachedClass == IntPtr.Zero)
                return null;
            object result = ConstructTrustedMonoClass(classPrototype, cachedClass);
            if (result == null || !IsReadyMonoClass(result, cachedClass))
                return null;
            IntPtr cachedClassAfter;
            if (!TryReadPointer(Address(data, GenericClassCachedClassOffset), out cachedClassAfter)
                || cachedClassAfter != cachedClass)
                return null;
            return result;
        }

        private static object ResolveArrayElementClass(object arrayType, object classPrototype,
            string expectedName, string expectedNamespace)
        {
            if (!IsSzArrayType(arrayType))
                return null;
            // On the qualified runtime SzArray.Data is already the element class.
            // Do not use MonoType.Class, whose helper path adds an unqualified
            // indirection and can accept a self-consistent but wrong class.
            object dataValue = GetMember(arrayType, "Data");
            IntPtr elementClass;
            if (!TryConvertPointer(dataValue, out elementClass) || elementClass == IntPtr.Zero)
                return null;
            object result = ConstructTrustedMonoClass(classPrototype, elementClass);
            if (result == null || !IsReadyMonoClass(result, elementClass)
                || ClassName(result) != expectedName
                || ClassNamespace(result) != expectedNamespace)
                return null;
            return result;
        }

        private static object ConstructTrustedMonoClass(object prototype, IntPtr address)
        {
            if (prototype == null || address == IntPtr.Zero)
                return null;
            Type type = prototype.GetType();
            ConstructorInfo constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { typeof(IntPtr) }, null);
            if (constructor == null)
                return null;
            object result = constructor.Invoke(new object[] { address });
            return IsReadyMonoClass(result, address) ? result : null;
        }

        private static bool IsReadyMonoClass(object monoClass, IntPtr expectedAddress)
        {
            if (monoClass == null)
                return false;
            IntPtr actualAddress;
            if (!TryConvertPointer(GetMember(monoClass, "Address"), out actualAddress)
                || actualAddress != expectedAddress)
                return false;
            string name = ClassName(monoClass);
            string nameSpace = ClassNamespace(monoClass);
            if (String.IsNullOrEmpty(name) || nameSpace == null)
                return false;
            return monoClass is IEnumerable;
        }

        private static bool IsDictionaryField(object field, int offset, bool isStatic)
        {
            return field != null && HasStaticState(field, isStatic)
                && GetOffset(field) == offset;
        }

        private static bool IsGenericInstType(object monoType)
        {
            return ElementTypeName(monoType) == "GenericInst"
                || ElementTypeName(monoType) == "21";
        }

        private static bool IsSzArrayType(object monoType)
        {
            return ElementTypeName(monoType) == "SzArray"
                || ElementTypeName(monoType) == "29";
        }

        private static string ElementTypeName(object monoType)
        {
            if (monoType == null)
                return null;
            object value = GetMember(monoType, "ElementType");
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool IsClosedStringType(object monoType)
        {
            string descriptor = ElementTypeName(monoType);
            return descriptor == "String" || descriptor == "14";
        }

        private static bool IsClosedInt32Type(object monoType)
        {
            string descriptor = ElementTypeName(monoType);
            return descriptor == "I4" || descriptor == "8";
        }

        private static string ClassName(object monoClass)
        {
            object name = GetMember(monoClass, "Name");
            return name == null ? null : Convert.ToString(name, CultureInfo.InvariantCulture);
        }

        private static string ClassNamespace(object monoClass)
        {
            object name = GetMember(monoClass, "Namespace");
            return name == null ? null : Convert.ToString(name, CultureInfo.InvariantCulture);
        }

        private static bool SameMonoClass(object first, object second)
        {
            IntPtr firstAddress;
            IntPtr secondAddress;
            return TryConvertPointer(GetMember(first, "Address"), out firstAddress)
                && TryConvertPointer(GetMember(second, "Address"), out secondAddress)
                && firstAddress != IntPtr.Zero && firstAddress == secondAddress;
        }

        private static bool IsStringClass(object monoClass)
        {
            string name = ClassName(monoClass);
            return name == "String" || name == "System_String";
        }

        private static bool IsStringType(object monoType)
        {
            if (monoType == null)
                return false;
            object klass = GetMember(monoType, "Class");
            if (klass != null && IsStringClass(klass))
                return true;
            object elementType = GetMember(monoType, "ElementType");
            string descriptor = elementType == null ? null :
                Convert.ToString(elementType, CultureInfo.InvariantCulture);
            return descriptor == "String" || descriptor == "STRING" || descriptor == "14";
        }

        private static bool IsStringArrayType(object monoType)
        {
            if (monoType == null)
                return false;
            string elementType = Convert.ToString(GetMember(monoType, "ElementType"),
                CultureInfo.InvariantCulture);
            if (!String.Equals(elementType, "SzArray", StringComparison.OrdinalIgnoreCase))
                return false;
            object arrayClass = GetMember(monoType, "Class");
            string name = ClassName(arrayClass);
            return arrayClass != null && name != null
                && name.IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsInt32Type(object monoType)
        {
            if (monoType == null)
                return false;
            object klass = GetMember(monoType, "Class");
            if (klass != null)
            {
                string name = ClassName(klass);
                if (name == "Int32" || name == "System_Int32")
                    return true;
            }
            object elementType = GetMember(monoType, "ElementType");
            string descriptor = elementType == null ? null :
                Convert.ToString(elementType, CultureInfo.InvariantCulture);
            return descriptor == "I4" || descriptor == "Int32" || descriptor == "8";
        }

        private static bool IsBooleanType(object monoType)
        {
            if (monoType == null)
                return false;
            object klass = GetMember(monoType, "Class");
            if (klass != null)
            {
                string name = ClassName(klass);
                if (name == "Boolean" || name == "System_Boolean")
                    return true;
            }
            object elementType = GetMember(monoType, "ElementType");
            string descriptor = elementType == null ? null :
                Convert.ToString(elementType, CultureInfo.InvariantCulture);
            return descriptor == "Boolean" || descriptor == "BOOLEAN" || descriptor == "2";
        }
    }
}
