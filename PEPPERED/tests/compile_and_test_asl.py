#!/usr/bin/env python3
"""Compile every real ASL action and run its adapter against explicit fixtures.
This is NOT the Windows LiveSplit process or a game-memory runtime test.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
ACTIONS = {'startup':'void','init':'void','update':'bool','start':'bool','split':'bool',
           'reset':'bool','isLoading':'bool','onStart':'void','onSplit':'void',
           'onReset':'void','exit':'void','shutdown':'void'}


def render_emitted_asl():
    """Render the current template in memory; never consume/write production ASL."""
    template = (ROOT / 'src' / 'PEPPERED.asl.in').read_text(encoding='utf-8')
    logic = ROOT / 'Components' / 'Peppered.AutoSplitter.dll'
    if not logic.is_file():
        raise RuntimeError('missing compiled logic dependency: ' + str(logic))
    supported = json.loads((ROOT / 'supported-build.json').read_text(encoding='utf-8'))['files']
    pairs = ',\n'.join(
        '        { ' + json.dumps(name) + ', ' + json.dumps(value) + ' }'
        for name, value in supported.items()
    )
    return template.replace('__LOGIC_SHA256__', hashlib.sha256(logic.read_bytes()).hexdigest()).replace(
        '__SUPPORTED_HASHES__', pairs
    )


def body(text, name):
    match = re.search(r'(?m)^' + re.escape(name) + r'\s*\n\{', text)
    if match is None:
        raise RuntimeError(name)
    start = text.index('{', match.start()); depth = 0; state = 'code'; i = start
    while i < len(text):
        c = text[i]; n = text[i:i+2]
        if state == 'line':
            if c == '\n': state = 'code'
        elif state == 'block':
            if n == '*/': state = 'code'; i += 1
        elif state in ('string', 'char'):
            if c == '\\': i += 1
            elif (state == 'string' and c == '"') or (state == 'char' and c == "'"): state = 'code'
        elif n == '//': state = 'line'; i += 1
        elif n == '/*': state = 'block'; i += 1
        elif c == '"': state = 'string'
        elif c == "'": state = 'char'
        elif c == '{': depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0: return text[start+1:i]
        i += 1
    raise AssertionError('Unclosed action ' + name)


def emitted_logger_setup(text):
    """Execute the emitted startup sink and banner expression, not a fake logger.

    The full startup loads the real asl-help/Unity dependency and is not safe
    in the Mono adapter. Only the independent logging statements are isolated;
    every ASL action body is still compiled without modification.
    """
    startup = body(text, 'startup')
    hash_start = startup.index('vars.HashBytes = (Func<byte[], string>)(bytes => {')
    hash_end = startup.index('\n    });', hash_start) + len('\n    });')
    sink_start = startup.index('vars.PepperedLog = (Action<string>)(text => {')
    sink_end = startup.index('\n    });', sink_start) + len('\n    });')
    sink = startup[sink_start:sink_end]
    guard = 'if ((bool)vars.PepperedLoggingReady && settings["diagnostics"])'
    if sink.count(guard) != 1 or sink.index(guard) > min(
        sink.index('print('), sink.index('File.Exists('), sink.index('new FileInfo('),
        sink.index('File.AppendAllText(')
    ) or not re.search(re.escape(guard) + r'\s*{', sink):
        raise RuntimeError('emitted logger is not guarded before print and file access')
    if re.search(r'\breturn\s*;', sink):
        raise RuntimeError('bare lambda return is rewritten by the official ASL compiler')
    if 'vars.PepperedLog(' in startup or 'print(' in startup[:sink_start] + startup[sink_end:]:
        raise RuntimeError('emitted startup must not log before settings are restored')
    boot = re.search(r'(?m)^\s*vars\.PepperedBootMessage\s*=\s*[^\n]+;', startup)
    if boot is None or 'vars.PepperedBootMessage' in startup[:sink_start]:
        raise RuntimeError('emitted startup must defer its boot message')
    flags = []
    for flag in ('PepperedLoggingReady', 'PepperedLogBannerSent'):
        statement = 'vars.' + flag + ' = false;'
        if startup.count(statement) != 1 or startup.index(statement) > sink_start:
            raise RuntimeError('emitted startup must initialize ' + flag + ' before sink')
        flags.append(statement)
    logic = json.dumps(str(ROOT / 'Components/Peppered.AutoSplitter.dll'))
    return '\n'.join([startup[hash_start:hash_end],
                      'byte[] logicBytes = File.ReadAllBytes(' + logic + ');'] +
                     flags + [sink, boot.group().strip()])


def validate_settings(text):
    if text.count('vars.MonoMetadata.Images.Clear();') != 1:
        raise RuntimeError('emitted ASL must contain exactly one public image-cache refresh')
    if 'boot 0.4.0-rc14 optional-friend-loss' not in text or 'loaded 0.4.0-rc14 optional-friend-loss' not in text:
        raise RuntimeError('emitted ASL is not the current RC14 optional-friend-loss template')
    startup = body(text, 'startup')
    entries = re.findall(r'settings\.Add\("([^"]+)",\s*(true|false),\s*"([^"]*)"(?:,\s*"([^"]+)")?\)', startup)
    if len(entries) != 137:
        raise RuntimeError('expected 137 generated ASL settings, got ' + str(len(entries)))
    values = {key: (value == 'true', label, parent) for key, value, label, parent in entries}
    if len(entries) != len(values):
        raise RuntimeError('duplicate generated ASL setting key')
    # Pin all 136 legacy IDs, defaults, labels, parents, and relative order.
    # The only allowed default change is diagnostics true -> false; normalize
    # that one known tuple before comparing the original RC13 digest.
    old_entries = [entry for entry in entries if entry[0] != 'ending.8.friend_loss']
    if values['diagnostics'][0] is not False:
        raise RuntimeError('diagnostics must be opt-in by default')
    old_entries = [(key, 'true' if key == 'diagnostics' else default, label, parent)
                   for key, default, label, parent in old_entries]
    old_digest = hashlib.sha256(json.dumps(old_entries, ensure_ascii=False,
        separators=(',', ':')).encode('utf-8')).hexdigest()
    if len(old_entries) != 136 or old_digest != '9289dbe53c9bb33e3d5b792dedb51ae2c96822043c6fb7755008a9281de04004':
        raise RuntimeError('legacy 136 ASL setting definitions/order changed')
    # No redundant singleton wrappers, empty route groups, or variant wrappers.
    for obsolete in ('endings', 'ending1', 'ending2', 'ending3', 'ending4',
                     'ending8', 'ending9', 'ending10', 'ending11',
                     'ending5With', 'ending5Against', 'ending6With',
                     'ending6Against', 'ending7With', 'ending7Against',
                     'sceneWorld0Detailed', 'sceneWorld4Branches', 'sceneWorld5Detailed'):
        if obsolete in values:
            raise RuntimeError('redundant/empty group survived: ' + obsolete)
    for key, (_, _, parent) in values.items():
        if parent and parent not in values:
            raise RuntimeError('missing parent for setting: ' + key)
        if key.startswith('ending.'):
            number = key.split('.')[1]
            expected_parent = 'splitEndings'
            if parent != expected_parent:
                raise RuntimeError('ending hierarchy too deep or wrong: ' + key)
    for key, expected in (
        ('enableTimers', False), ('diagnostics', False), ('splitScenes', True), ('splitEndings', True),
        ('scene.w0.coarse.office2', True),
        ('scene.w1.detailed.a3_1', False), ('scene.w3.branch.b3', False),
        ('scene.w3.branch.subspace4', False), ('scene.w3.branch.subspace5', False),
        ('scene.w3.branch.subspace_final', False), ('ending.1', False), ('ending.8', False),
        ('ending.8.friend_loss', False),
        ('ending.5.with', False), ('ending.5.against', False),
        ('ending.6.against', False), ('ending.7.against', False),
        ('ending.11', False),
    ):
        if key not in values or values[key][0] != expected:
            raise RuntimeError('wrong generated ASL setting default: ' + key)
    starts = ('Office_1', 'A_1', 'A_7', 'B_0', 'G_0', 'G_End')
    for number, scene in enumerate(starts):
        world_key = 'sceneWorld' + str(number)
        if values[world_key][1] != 'World %d (starts at %s)' % (number, scene):
            raise RuntimeError('world group does not explain its starting scene: ' + world_key)
        for suffix, label in (('Main', 'Main scenes'), ('Detailed', 'Detailed scenes'), ('Branches', 'Optional branches')):
            key = world_key + suffix
            if key in values and values[key][1] != label:
                raise RuntimeError('unclear scene-group label: ' + key)
    if 'scene.w5.branch.t_end' in values:
        raise RuntimeError('ordinary T_End setting must be removed')
    for key in ('loadRemoval', 'bossBigman', 'bossPrison', 'bossMerdeka3',
                'bossTheodore', 'bossFirstUnsupported', 'splitBosses'):
        if key in values:
            raise RuntimeError('obsolete setting survived: ' + key)
    for key in ('ending5', 'ending6', 'ending7'):
        if key in values:
            raise RuntimeError('inert endpoint or redundant group survived: ' + key)
    leaf_keys = [key for key in values if key.startswith('ending.')]
    expected_ending_order = ['ending.' + key for key in
                             ('1','2','3','4','5.with','5.against',
                              '6.with','6.against','7.with','7.against',
                              '8','8.friend_loss','9','10','11')]
    if leaf_keys != expected_ending_order or any(values[key][0] for key in leaf_keys):
        raise RuntimeError('only the fifteen implemented ending leaves may be exposed, all default-off')
    expected_labels = {
        'ending.5.with': 'Ending 5 (With Karmina): start watching TV with Karmina',
        'ending.5.against': 'Ending 5 (Against Karmina): final CONTINUE / GIVE UP choice',
        'ending.6.with': 'Ending 6 (With Karmina): start watching TV with Karmina',
        'ending.6.against': 'Ending 6 (Against Karmina): final CONTINUE / GIVE UP choice',
        'ending.7.with': 'Ending 7 (With Karmina): start watching TV with Karmina',
        'ending.7.against': 'Ending 7 (Against Karmina): final CONTINUE / GIVE UP choice',
        'ending.8': 'Ending 8: Merdeka surrender — final GIVE HER THE STARS confirmation',
        'ending.8.friend_loss': 'Ending 8 (optional friendship route): Merdeka defeat - NOT Ending 7',
    }
    for key, label in expected_labels.items():
        if values[key][1] != label:
            raise RuntimeError('ending setting label changed: ' + key)
    if values['ending.8'][1] != expected_labels['ending.8']:
        raise RuntimeError('Ending 8 setting label changed')
    source = (ROOT / 'src/Logic.cs').read_text()
    route_ids = re.findall(
        r'new SceneDefinition\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*\d+,\s*SceneCategory\.(Coarse|Detailed|Branch),\s*(true|false)\)',
        source,
    )
    if len(route_ids) != 88:
        raise RuntimeError('expected 88 route definitions, got ' + str(len(route_ids)))
    if 'T_End' in source or 'scene.w5.branch.t_end' in source:
        raise RuntimeError('ordinary T_End definition survived')
    for key, scene, label, category, default in route_ids:
        if key not in values or values[key][0] != (default == 'true'):
            raise RuntimeError('missing/default mismatch for generated route setting: ' + key)
        if values[key][1] != label:
            raise RuntimeError('generated ASL route label mismatch: ' + key)
        if not label.startswith(scene + ' (') or not label.endswith(')'):
            raise RuntimeError('route label lacks concise English description: ' + key)
        group = re.search(r'\.w(\d+)\.', key)
        if group is None:
            raise RuntimeError('route setting has no world group: ' + key)
        group_key = 'sceneWorld' + group.group(1) + {
            'Coarse': 'Main', 'Detailed': 'Detailed', 'Branch': 'Branches'
        }[category]
        if values[key][2] != group_key:
            raise RuntimeError('route setting has wrong parent: ' + key)
        if group_key not in values:
            raise RuntimeError('missing route setting group: ' + group_key)
    if 'options.SceneIds = new HashSet<string>();' not in text or 'options.EndingIds = new HashSet<string>();' not in text:
        raise RuntimeError('generated ASL does not construct scene/ending option sets')
    for physical in ('ending.execution_black', 'ending.family_black',
                     'ending.bar_dialogue', 'ending.final_death_black',
                     'ending.karmina_tv_start',
                     'ending.merdeka_surrender',
                     'ending.theodore_victory',
                     'ending.theodore_death', 'ending.gem_dialogue'):
        if physical not in text:
            raise RuntimeError('missing physical ending event mapping: ' + physical)
    expected_mapping = (
        ('ending.5.with', 'ending.karmina_tv_start'),
        ('ending.5.against', 'ending.karmina_final_choice'),
        ('ending.6.with', 'ending.karmina_tv_start'),
        ('ending.6.against', 'ending.karmina_final_choice'),
        ('ending.7.with', 'ending.karmina_tv_start'),
        ('ending.7.against', 'ending.karmina_final_choice'),
        ('ending.8', 'ending.merdeka_surrender'),
        ('ending.8.friend_loss', 'ending.merdeka_defeat'),
    )
    for setting, physical in expected_mapping:
        mapping = 'if (settings["' + setting + '"]) options.EndingIds.Add("' + physical + '");'
        if mapping not in text:
            raise RuntimeError('wrong physical ending option mapping: ' + setting)
    obsolete_ending7_mapping = 'if (settings["ending.7.with"]) options.EndingIds.Add("ending.merdeka_defeat");'
    if obsolete_ending7_mapping in text:
        raise RuntimeError('Ending7 still maps raw Merdeka defeat instead of the TV-start event')
    if 'ending.karmina_tv"' in text or 'ending.karmina_tv",' in text:
        raise RuntimeError('obsolete Karmina TV mapping survived in emitted ASL')
    if 'return false;' not in body(text, 'isLoading'):
        raise RuntimeError('load-removal action changed from conservative false')


HARNESS = r'''

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Peppered;
public enum TimerPhase { NotRunning, Running, Paused, Ended }
public sealed class TimerState { public TimerPhase CurrentPhase; }
public sealed class TimerModel {
 public object CurrentState; public Action ResetEvent;
 public void Reset() { ((TimerState)CurrentState).CurrentPhase=TimerPhase.NotRunning; if(ResetEvent!=null)ResetEvent(); }
}
public sealed class SettingsStub {
 public bool StartEnabled=true,SplitEnabled=true,ResetEnabled=true;
 Dictionary<string,bool> values=new Dictionary<string,bool>();
 public bool this[string key] {get{return values.ContainsKey(key)&&values[key];}set{values[key]=value;}}
 public void Add(string key,bool value,string description,string parent=null) {values[key]=value;}
}
public sealed class ModuleStub {public string ModuleName="UnityPlayer.dll";public string FileName;}
public sealed class FakeScene {public bool IsValid=true;public IntPtr Address=IntPtr.Zero;public string Name="[Main Menu]";}
public sealed class FakeScenes {public FakeScene Active=new FakeScene();}
public sealed class FakeImages {
 public int ClearCalls; public int Generation; public bool ThrowOnClear; public Action ClearHook;
 public void Clear(){ClearCalls++;if(ThrowOnClear)throw new InvalidOperationException("image refresh failed");Generation++;if(ClearHook!=null)ClearHook();}
}
public sealed class FakeMonoMetadata {
 public readonly FakeImages Images=new FakeImages();
 public FakeMonoMetadata(){}
 public FakeMonoMetadata(Action clearHook){Images.ClearHook=clearHook;}
}
public sealed class FakeHelper {public bool Loaded=true;public FakeScenes Scenes=new FakeScenes(); public Func<dynamic,bool> TryLoad;public void Dispose(){} }
public sealed class FakeReader {
 public ReadResult Sample=new ReadResult{Valid=true,Abyss=0,HasCheckpoint=false,StartReady=false,
  GolfBatteries=null,GolfCutscenePlaying=null,
  TheodoreHp=null,TheodoreMetadataReady=true,
  TvStartActive=false,TvStartMetadataReady=true,
  MerdekaStage=null,
  KarminaChoice=null,KarminaMetadataReady=true,
  SurrenderChoice=null,SurrenderMetadataReady=true,
  EndingSignals=new HashSet<string>(),BoundaryMetadataReady=true};
 public int ConfigureCalls; public int ReadyAfterConfigure=1; public int TheodoreBindingResetCalls;public int SurrenderBindingResetCalls;public int MerdekaBindingResetCalls;public int KarminaBindingResetCalls;
 public void ResetTheodoreBinding(){TheodoreBindingResetCalls++;}
 public void ResetSurrenderBinding(){SurrenderBindingResetCalls++;}
 public void ResetMerdekaBinding(){MerdekaBindingResetCalls++;}
 public void ResetKarminaBinding(){KarminaBindingResetCalls++;}
 public ReadResult Read(string scene) {
  if(scene==""&&ConfigureCalls<ReadyAfterConfigure)
   return new ReadResult{Valid=Sample.Valid,Abyss=Sample.Abyss,Bigman=Sample.Bigman,
    Prison=Sample.Prison,HasCheckpoint=null,DialoguePlaying=null,StartReady=null,
    GolfBattleActive=Sample.GolfBattleActive,GolfBatteries=Sample.GolfBatteries,
    GolfCutscenePlaying=Sample.GolfCutscenePlaying,
    TheodoreHp=Sample.TheodoreHp,TheodoreMetadataReady=false,
    TvStartActive=null,TvStartMetadataReady=false,
    MerdekaStage=Sample.MerdekaStage,
    KarminaChoice=null,KarminaMetadataReady=false,
    SurrenderChoice=null,SurrenderMetadataReady=false,
    EndingSignals=new HashSet<string>(),BoundaryMetadataReady=false};
  return Sample;
 }
 public void Configure(dynamic helper,dynamic mono){ConfigureCalls++;}
}
public sealed class FakeCode {
 public object Candidate; public Func<object> CandidateFactory; public object LastCandidate;
 public object CreateInstance(string name){
  if(name=="Peppered.ReadOnlyReader"){
   LastCandidate=CandidateFactory==null?Candidate:CandidateFactory();return LastCandidate;
  }
  if(name=="Peppered.Snapshot")return new Snapshot();
  if(name=="Peppered.Options")return new Options{Worlds=new HashSet<int>(),SceneIds=new HashSet<string>(),EndingIds=new HashSet<string>()};
  return new Decision();
 }
}
public sealed class ActualReaderFacade {
 private readonly ReadOnlyReader fixedReader;
 private readonly ActualReaderFixture fixture;
 private ReadOnlyReader resolvedReader;
 public int TheodoreBindingResetCalls;public int SurrenderBindingResetCalls;public int MerdekaBindingResetCalls;public int KarminaBindingResetCalls;
 public Action<string> AfterRead;
 public ActualReaderFacade(ReadOnlyReader reader){fixedReader=reader;}
 public ActualReaderFacade(ActualReaderFixture fixture){this.fixture=fixture;}
 public ReadResult Read(string scene){
  ReadOnlyReader active=resolvedReader??fixedReader??fixture.Initial;ReadResult result=active.Read(scene);
  if(AfterRead!=null)AfterRead(scene);return result;
 }
 public void ResetTheodoreBinding(){
  TheodoreBindingResetCalls++;
  ReadOnlyReader active=resolvedReader??fixedReader??fixture.Initial;
  if(active!=null)active.ResetTheodoreBinding();
 }
 public void ResetSurrenderBinding(){
  SurrenderBindingResetCalls++;
  ReadOnlyReader active=resolvedReader??fixedReader??fixture.Initial;
  if(active!=null)active.ResetSurrenderBinding();
 }
 public void ResetMerdekaBinding(){
  MerdekaBindingResetCalls++;
  ReadOnlyReader active=resolvedReader??fixedReader??fixture.Initial;
  if(active!=null)active.ResetMerdekaBinding();
 }
 public void ResetKarminaBinding(){
  KarminaBindingResetCalls++;
  ReadOnlyReader active=resolvedReader??fixedReader??fixture.Initial;
  if(active!=null)active.ResetKarminaBinding();
 }
 // Configure observes the public fake image-cache generation. A stale wrapper
 // keeps the core read valid but cannot resolve optional boundary metadata.
 public void Configure(dynamic helper,dynamic mono){
  if(fixture!=null)resolvedReader=fixture.ReaderFor((int)mono.Images.Generation);
 }
}
public sealed class ActualReaderMemory : ReadOnlyReader.IReaderMemory {
 private readonly Dictionary<long,int> ints=new Dictionary<long,int>();
 private readonly Dictionary<long,long> pointers=new Dictionary<long,long>();
 private readonly Dictionary<long,bool> bools=new Dictionary<long,bool>();
 private readonly Dictionary<long,string> strings=new Dictionary<long,string>();
 public void Int(long address,int value){ints[address]=value;}
 public void Pointer(long address,long value){pointers[address]=value;}
 public void Bool(long address,bool value){bools[address]=value;}
 public void String(long address,string value){strings[address]=value;}
 public void RemoveInt(long address){ints.Remove(address);}
 public bool TryReadInt(IntPtr address,out int value){return ints.TryGetValue(address.ToInt64(),out value);}
 public bool TryReadBool(IntPtr address,out bool value){return bools.TryGetValue(address.ToInt64(),out value);}
 public bool TryReadPointer(IntPtr address,out IntPtr value){
  long raw;bool ok=pointers.TryGetValue(address.ToInt64(),out raw);value=ok?new IntPtr(raw):IntPtr.Zero;return ok;
 }
 public bool TryReadString(IntPtr address,out string value){return strings.TryGetValue(address.ToInt64(),out value);}
}
public sealed class ActualReaderFixture {
 public const long Scene=0x1000,ChoicesStatic=0x1008,ChoicesObject=0x2000,ChoicesEntries=0x3000,Key=0x5000;
 public const long Selectables=0x6000,TheodoreObject=0x7000,TheodoreNavigation=0x7100,TheodoreFill=0x7200,TheodoreVtable=0x7300,TheodoreFillVtable=0x7400;
 public const long SurrenderStatic=0x9000,SurrenderObject=0xA000,SurrenderVtable=0xA100;
 public const long SurrenderChooser=0xB000,SurrenderChooserVtable=0xB100,SurrenderLines=0xC000;
 public const long SurrenderLine=0xD000,SurrenderLeft=0xD100,SurrenderRight=0xD200,SurrenderLineAlt=0xD300;
 public const long SurrenderObjectAlt=0xA200,SurrenderVtableAlt=0xA300,SurrenderChooserAlt=0xB200,SurrenderChooserVtableAlt=0xB300;
 public const long SurrenderDialogueClass=0x11020,SurrenderChooserClass=0x11021;
 public const long TvStartStatic=0x12000,TvStartObject=0x13000,TvStartVtable=0x13100,
  TvStartLines=0x14000,TvStartLine=0x15000,TvStartDialogueClass=0x12030;
 public const int SelectablesOffset=40,SelectableCountOffset=48;
 public const int EntryStride=24;
 public readonly ActualReaderMemory Memory;
 public readonly ReadOnlyReader Initial,Full,SurrenderOnly,KarminaOnly,TvOnly,Invalid;
 private ActualReaderFixture(ActualReaderMemory memory,ReadOnlyReader initial,ReadOnlyReader full,ReadOnlyReader surrenderOnly,ReadOnlyReader karminaOnly,ReadOnlyReader tvOnly,ReadOnlyReader invalid){
  Memory=memory;Initial=initial;Full=full;SurrenderOnly=surrenderOnly;KarminaOnly=karminaOnly;TvOnly=tvOnly;Invalid=invalid;
 }
 public static ActualReaderFixture Create(){
  var memory=new ActualReaderMemory();
  memory.Int(Scene,300);memory.Bool(Scene+32,false);memory.Int(Scene+36,0);
  memory.Pointer(ChoicesStatic,ChoicesObject);
  memory.Int(ChoicesObject+64,1);memory.Int(ChoicesObject+76,1);memory.Pointer(ChoicesObject+24,ChoicesEntries);
  memory.Int(ChoicesEntries+24,1);
  long entry=ChoicesEntries+32;
  memory.Int(entry,1);memory.Int(entry+4,-1);memory.Pointer(entry+8,Key);memory.Int(entry+16,0);
  memory.Int(Key+16,9);memory.String(Key,"Batteries");
  memory.Pointer(Scene+SelectablesOffset,Selectables);memory.Int(Scene+SelectableCountOffset,1);
  memory.Int(Selectables+24,1);memory.Pointer(Selectables+32,TheodoreObject);
  memory.Pointer(TheodoreObject+0,TheodoreVtable);memory.Pointer(TheodoreVtable+0,0x1003);memory.Pointer(TheodoreObject+16,0x8000);
  memory.Int(TheodoreObject+24,0);
  memory.Pointer(TheodoreObject+104,0);memory.Int(TheodoreObject+220,0);
  memory.Pointer(TheodoreObject+232,TheodoreFill);memory.Pointer(TheodoreObject+240,0);
  memory.Int(TheodoreObject+296,0);memory.Int(TheodoreObject+300,FloatBits(0.0f));
  memory.Int(TheodoreObject+304,FloatBits(100.0f));memory.Bool(TheodoreObject+308,true);
  memory.Int(TheodoreObject+312,FloatBits(100.0f));
  memory.Pointer(TheodoreFill+0,TheodoreFillVtable);memory.Pointer(TheodoreFillVtable+0,0x1006);memory.Pointer(TheodoreFill+16,0x8100);
  memory.Pointer(SurrenderStatic,SurrenderObject);
  memory.Pointer(SurrenderObject+0,SurrenderVtable);memory.Pointer(SurrenderVtable+0,SurrenderDialogueClass);
  memory.Pointer(SurrenderObject+96,SurrenderLines);memory.Int(SurrenderLines+24,1);memory.Pointer(SurrenderLines+32,SurrenderLine);
  memory.Int(SurrenderObject+188,0);memory.Pointer(SurrenderObject+80,SurrenderLeft);memory.Pointer(SurrenderObject+88,SurrenderRight);
  memory.Bool(SurrenderObject+196,true);memory.Bool(SurrenderObject+200,false);
  memory.Pointer(SurrenderObject+40,SurrenderChooser);memory.Pointer(SurrenderChooser+0,SurrenderChooserVtable);
  memory.Pointer(SurrenderChooserVtable+0,SurrenderChooserClass);memory.Int(SurrenderChooser+32,0);
  memory.String(SurrenderLine,"B_M/m45");memory.String(SurrenderLeft,"B_M/m41");memory.String(SurrenderRight,"B_M/m42");
  memory.String(SurrenderLineAlt,"B_E/m161");
  // Minimal Ending 5/6 TV-start layout: static instance, runtime class,
  // one-line dialogue array, current line, and Playing only. There is no
  // chooser, TMP component, rating dictionary, or star metadata in TvOnly.
  memory.Pointer(TvStartStatic,TvStartObject);memory.Pointer(TvStartObject,TvStartVtable);
  memory.Pointer(TvStartVtable,TvStartDialogueClass);memory.Pointer(TvStartObject+96,TvStartLines);
  memory.Int(TvStartLines+24,1);memory.Pointer(TvStartLines+32,TvStartLine);
  memory.Int(TvStartObject+188,0);memory.Bool(TvStartObject+196,false);
  memory.String(TvStartLine,"B_A/m32");
  var choices=ReadOnlyReader.TestDictionaryLayout(new IntPtr(Scene),8);
  var theodore=ReadOnlyReader.TestTheodoreLayout(new IntPtr(Scene),SelectablesOffset,SelectableCountOffset);
  var surrender=ReadOnlyReader.TestSurrenderLayout(new IntPtr(SurrenderStatic),0,
   new IntPtr(SurrenderDialogueClass),new IntPtr(SurrenderChooserClass));
  var tvStart=ReadOnlyReader.TestTvStartLayout(new IntPtr(TvStartStatic),0,
   new IntPtr(TvStartDialogueClass),96,188,196);
  var initial=ReadOnlyReader.ForTests(memory,new IntPtr(Scene),0,null,null,-1,null,null,null);
  var full=ReadOnlyReader.ForTests(memory,new IntPtr(Scene),0,choices,null,-1,null,null,
   ReadOnlyReader.TestEndingLayout(new IntPtr(Scene),32,36),theodore,surrender,tvStart);
  var surrenderOnly=ReadOnlyReader.ForTests(memory,new IntPtr(Scene),0,null,null,-1,null,null,
   null,null,surrender);
  // Karmina's final choice uses the same qualified dialogue/chooser metadata
  // layout as Ending 8, but a separate reader instance and binding lifecycle.
  var karminaOnly=ReadOnlyReader.ForTests(memory,new IntPtr(Scene),0,null,null,-1,null,null,
   null,null,surrender);
  var tvOnly=ReadOnlyReader.ForTests(memory,new IntPtr(Scene),0,null,null,-1,null,null,
   null,null,null,tvStart);
  var invalid=ReadOnlyReader.ForTests(memory,IntPtr.Zero,0,null,null);
  return new ActualReaderFixture(memory,initial,full,surrenderOnly,karminaOnly,tvOnly,invalid);
 }
 public ReadOnlyReader ReaderFor(int imageGeneration){return imageGeneration>0?Full:Initial;}
 private static int FloatBits(float value){return BitConverter.ToInt32(BitConverter.GetBytes(value),0);}
 private void SetTheodoreHp(float hp){Memory.Int(TheodoreObject+312,FloatBits(hp));}
 public void SetGolf(bool cutscene,int batteries){
  Memory.Int(Scene,300);Memory.Bool(Scene+32,cutscene);Memory.Int(Scene+36,0);
  Memory.Int(ChoicesEntries+32+16,batteries);
  SetTheodoreHp(cutscene?50.0f:100.0f);
 }
 public void SetBattle(){SetGolf(false,2);}
 public void SetZeroBatteryBattle(){SetGolf(false,0);}
 public void SetLoss(){SetGolf(true,-1);}
 public void SetWin(){Memory.Int(Scene,300);Memory.Bool(Scene+32,true);Memory.Int(Scene+36,0);Memory.Int(ChoicesEntries+32+16,0);SetTheodoreHp(0.0f);}
 public void SetNegativeBattle(){SetGolf(false,-2);}
 public void SetNegativeLoss(){SetGolf(true,-3);}
 public void SetLossBeforeCutscene(){SetGolf(false,-1);}
 public void SetMerdekaStage(int stage){Memory.Int(Scene,stage);}
 public void SetTv(bool playing,int lineIndex,string line,bool texting=false){
  // The qualified TV layout has no Texting field. Passing true documents
  // that a concurrent Texting flag is deliberately irrelevant to this gate.
  Memory.Int(Scene,300);Memory.Bool(TvStartObject+196,playing);
  Memory.Int(TvStartObject+188,lineIndex);Memory.String(TvStartLine,line);
 }
 public void SetTvArrayLength(int length){Memory.Int(TvStartLines+24,length);}
 public void SetTvRootClass(bool valid){Memory.Pointer(TvStartVtable,valid?TvStartDialogueClass:0);}
 public void SetTvManager(bool valid){Memory.Pointer(TvStartStatic,valid?TvStartObject:0);}
 public void SetSurrender(bool playing,bool texting,int lineIndex,int choice,string line,string left,string right){
  Memory.Int(Scene,300);Memory.Bool(SurrenderObject+196,playing);Memory.Bool(SurrenderObject+200,texting);
  Memory.Int(SurrenderObject+188,lineIndex);Memory.Int(SurrenderChooser+32,choice);
  Memory.String(SurrenderLine,line);Memory.String(SurrenderLeft,left);Memory.String(SurrenderRight,right);
 }
 public void SetSurrenderPending(){SetSurrender(true,false,0,0,"B_M/m45","B_M/m41","B_M/m42");}
 public void SetSurrenderChoice(int choice,bool playing){SetSurrender(playing,false,0,choice,"B_M/m45","B_M/m41","B_M/m42");}
 public void SetKarmina(bool playing,bool texting,int lineIndex,int choice,string line,string left,string right){
  Memory.Int(Scene,300);Memory.Bool(SurrenderObject+196,playing);Memory.Bool(SurrenderObject+200,texting);
  Memory.Int(SurrenderObject+188,lineIndex);Memory.Int(SurrenderChooser+32,choice);
  Memory.String(SurrenderLine,line);Memory.String(SurrenderLeft,left);Memory.String(SurrenderRight,right);
  Memory.Pointer(SurrenderLines+32,SurrenderLine);
 }
 public void SetKarminaPending(){SetKarmina(true,false,0,0,"B_E/m161","B_E/m162","B_E/m163");}
 public void SetKarminaChoice(int choice,bool playing){SetKarmina(playing,false,0,choice,"B_E/m161","B_E/m162","B_E/m163");}
 public void SetKarminaIdentity(bool alternate,int choice){
  long dialogue=alternate?SurrenderObjectAlt:SurrenderObject;
  long vtable=alternate?SurrenderVtableAlt:SurrenderVtable;
  long chooser=alternate?SurrenderChooserAlt:SurrenderChooser;
  long chooserVtable=alternate?SurrenderChooserVtableAlt:SurrenderChooserVtable;
  Memory.Pointer(SurrenderStatic,dialogue);Memory.Pointer(dialogue,vtable);Memory.Pointer(vtable,SurrenderDialogueClass);
  Memory.Pointer(dialogue+96,SurrenderLines);Memory.Int(SurrenderLines+24,1);Memory.Pointer(SurrenderLines+32,SurrenderLine);
  Memory.Int(dialogue+188,0);Memory.Pointer(dialogue+80,SurrenderLeft);Memory.Pointer(dialogue+88,SurrenderRight);
  Memory.Bool(dialogue+196,choice==0);Memory.Bool(dialogue+200,false);Memory.Pointer(dialogue+40,chooser);
  Memory.Pointer(chooser,chooserVtable);Memory.Pointer(chooserVtable,SurrenderChooserClass);Memory.Int(chooser+32,choice);
  Memory.String(SurrenderLine,"B_E/m161");Memory.String(SurrenderLeft,"B_E/m162");Memory.String(SurrenderRight,"B_E/m163");
 }
 public void SetKarminaArrayIdentity(int choice){
  Memory.Int(Scene,300);Memory.Bool(SurrenderObject+196,false);Memory.Bool(SurrenderObject+200,false);
  Memory.Int(SurrenderObject+188,0);Memory.Int(SurrenderChooser+32,choice);
  Memory.Pointer(SurrenderLines+32,SurrenderLineAlt);Memory.String(SurrenderLineAlt,"B_E/m161");
  Memory.String(SurrenderLeft,"B_E/m162");Memory.String(SurrenderRight,"B_E/m163");
 }
 public void SetSurrenderIdentity(bool alternate,int choice){
  long dialogue=alternate?SurrenderObjectAlt:SurrenderObject;
  long vtable=alternate?SurrenderVtableAlt:SurrenderVtable;
  long chooser=alternate?SurrenderChooserAlt:SurrenderChooser;
  long chooserVtable=alternate?SurrenderChooserVtableAlt:SurrenderChooserVtable;
  Memory.Pointer(SurrenderStatic,dialogue);Memory.Pointer(dialogue,vtable);Memory.Pointer(vtable,SurrenderDialogueClass);
  Memory.Pointer(dialogue+96,SurrenderLines);Memory.Int(SurrenderLines+24,1);Memory.Pointer(SurrenderLines+32,SurrenderLine);
  Memory.Int(dialogue+188,0);Memory.Pointer(dialogue+80,SurrenderLeft);Memory.Pointer(dialogue+88,SurrenderRight);
  Memory.Bool(dialogue+196,choice==0);Memory.Bool(dialogue+200,false);Memory.Pointer(dialogue+40,chooser);
  Memory.Pointer(chooser,chooserVtable);Memory.Pointer(chooserVtable,SurrenderChooserClass);Memory.Int(chooser+32,choice);
  Memory.String(SurrenderLine,"B_M/m45");Memory.String(SurrenderLeft,"B_M/m41");Memory.String(SurrenderRight,"B_M/m42");
 }
 public void SetInvalid(){Memory.RemoveInt(Scene);}
}
public sealed class Actions {
 public dynamic vars=new ExpandoObject();public dynamic current=new ExpandoObject();public dynamic old=new ExpandoObject();
 public SettingsStub settings=new SettingsStub();public TimerState timer=new TimerState();
 public List<ModuleStub> modules=new List<ModuleStub>();public object game;public int refreshRate;
 public List<string> Logs=new List<string>();public int Starts,Resets,Splits;
 public void print(string s){Logs.Add(s);}
 public void Setup() {
  vars.Code=typeof(StateMachine).Assembly;vars.Core=new StateMachine();vars.Reader=new FakeReader();
  vars.Helper=new FakeHelper();vars.Decision=new Decision();vars.SupportedBuild=true;vars.ReaderConfigured=true;
  vars.MonoMetadata=null;vars.LastMetadataScene="";vars.LastTheodoreMetadataScene="";vars.LastSurrenderMetadataScene="";vars.LastKarminaMetadataScene="";vars.LastTvStartMetadataScene="";vars.TheodoreSceneAddress=IntPtr.Zero;vars.MetadataRetryAfter=0L;vars.OptionalMetadataRetryAfter=0L;
  vars.AutoStarting=false;vars.AutoSplitting=false;vars.RebindOnAttach=false;vars.LastTrace="";vars.LastFault="";
  // Reuse startup's actual emitted sink (and boot expression) with a private cwd.
__LOGGER_SETUP__
  // A saved ON checkbox is not enough to authorize writes before first update.
  settings["diagnostics"]=true;
  ((Action<string>)vars.PepperedLog)("before first update");
  settings["diagnostics"]=false;
  // The real helper owns vars.Log; script diagnostics must not use that slot.
  vars.Log=(Action<object>)(s=>{throw new Exception("reserved helper logger used");});
  var model=new TimerModel{CurrentState=timer};model.ResetEvent=()=>{Resets++;Action_onReset();};vars.TimerModel=model;
  vars.HashFile=(Func<string,string>)(p=>{using(var f=File.OpenRead(p))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();});
__SETTINGS__
 }
 public void Tick(string scene){
  ((FakeHelper)vars.Helper).Scenes.Active.Name=scene;
  if(!Action_update())return;
  if(timer.CurrentPhase==TimerPhase.Running || timer.CurrentPhase==TimerPhase.Paused) {
   if(Action_reset()&&settings.ResetEnabled){vars.TimerModel.Reset();return;}
   if(Action_split()&&settings.SplitEnabled){Splits++;Action_onSplit();}
  } else if(timer.CurrentPhase==TimerPhase.NotRunning && Action_start()&&settings.StartEnabled) {
   timer.CurrentPhase=TimerPhase.Running;Starts++;Action_onStart();
  }
 }
 public void ManualStart(){timer.CurrentPhase=TimerPhase.Running;Action_onStart();}
__METHODS__
}
public static class AdapterTests {
 static int count;
 static void Check(bool condition,string label){count++;if(!condition)throw new Exception("FAIL "+label);}
 static bool HasLog(Actions a,string text){return a.Logs.Any(s=>s.IndexOf(text,StringComparison.Ordinal)>=0);}
 // Legacy trace assertions explicitly opt in; the startup fixture itself stays OFF.
 static Actions New(){var a=new Actions();a.Setup();a.settings["enableTimers"]=true;a.settings["diagnostics"]=true;return a;}
 static void FreshStart(Actions a){a.Tick("[Main Menu]");a.Tick("Office_1");Check(a.Starts==0,"intro not ready");((FakeReader)a.vars.Reader).Sample.StartReady=true;a.Tick("Office_1");Check(a.Starts==1,"fresh delayed start");}
 static void LoggingPrivacyRegression(){
  // The process cwd is a private temporary sandbox, never the user's Components.
  const string path="Components/PEPPERED-autosplitter.log";
  Check(!File.Exists(path),"private log initially absent");
  var a=new Actions();a.Setup();
  Check(!a.settings["diagnostics"]&&!(bool)a.vars.PepperedLoggingReady
    &&!(bool)a.vars.PepperedLogBannerSent&&a.Logs.Count==0&&!File.Exists(path),
    "emitted sink silent during startup and before settings restore");
  a.settings["diagnostics"]=true;((Action<string>)a.vars.PepperedLog)("saved preset ON before first update");
  Check(a.Logs.Count==0&&!File.Exists(path),"restored ON preset cannot log before first update");
  // No game binaries are fabricated: an unsupported init must fail closed,
  // while the actual successful init remains covered only by the optional
  // PEPPERED_GAME_ROOT original-file fixture below.
  var rejected=new Actions();rejected.Setup();
  rejected.modules.Add(new ModuleStub{FileName=Path.Combine(Directory.GetCurrentDirectory(),"UnityPlayer.dll")});
  bool unsupported=false;
  try {rejected.Action_init();}catch(InvalidDataException){unsupported=true;}
  Check(unsupported&&rejected.Logs.Count==0&&!File.Exists(path),
   "unsupported-build init cannot produce a cold-start log");
  a.settings["diagnostics"]=false;((Action<string>)a.vars.PepperedLog)("cold OFF");
  var unavailable=new FakeReader();unavailable.Sample.Valid=false;
  a.vars.Code=new FakeCode{Candidate=unavailable};a.vars.MonoMetadata=new FakeMonoMetadata();
  a.vars.ReaderConfigured=false;a.Tick("[Main Menu]");
  Check(((string)a.vars.LastFault).StartsWith("metadata wait:")&&a.Logs.Count==0&&!File.Exists(path),
   "real update metadata-wait caller cannot create log or print when OFF");
  Directory.CreateDirectory("Components");
  File.WriteAllText(path,"PRIVATE EXISTING LOG\n");
  File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddHours(-2));
  byte[] original=File.ReadAllBytes(path);DateTime originalMtime=File.GetLastWriteTimeUtc(path);
  a.vars.Code=typeof(StateMachine).Assembly;a.vars.Reader=new FakeReader();a.vars.ReaderConfigured=true;
  a.vars.MonoMetadata=null;a.settings["enableTimers"]=false;
  a.Tick("Office_1");((Action<string>)a.vars.PepperedLog)("existing OFF");a.Action_exit();
  Check(a.Logs.Count==0&&original.SequenceEqual(File.ReadAllBytes(path))
   &&File.GetLastWriteTimeUtc(path)==originalMtime&&a.Starts==0&&a.Resets==0&&a.Splits==0,
   "OFF update/detach and direct logger leave existing file bytes, mtime and print untouched");
  // First opt-in after OFF startup emits the real deferred banners exactly once.
  a.settings["diagnostics"]=true;a.Tick("Office_1");
  Check((bool)a.vars.PepperedLogBannerSent&&HasLog(a,"boot 0.4.0-rc14")
   &&HasLog(a,"loaded 0.4.0-rc14")&&a.Logs.Count(s=>s.Contains("boot 0.4.0-rc14"))==1
   &&a.Logs.Count(s=>s.Contains("loaded 0.4.0-rc14"))==1
   &&File.ReadAllText(path).Contains("PEPPERED_ASL boot 0.4.0-rc14")
   &&File.ReadAllText(path).Contains("PEPPERED_ASL loaded 0.4.0-rc14")
   &&File.ReadAllBytes(path).Length>original.Length,
   "ON first update creates bounded sink output and deferred boot/loaded banners");
  ((Action<string>)a.vars.PepperedLog)("enabled trace");
  Check(HasLog(a,"enabled trace")&&File.ReadAllText(path).Contains("PEPPERED_ASL enabled trace"),
   "enabled direct call reaches actual emitted file and print sink");
  int priorPrints=a.Logs.Count;byte[] priorBytes=File.ReadAllBytes(path);
  DateTime priorMtime=File.GetLastWriteTimeUtc(path);
  a.settings["diagnostics"]=false;((Action<string>)a.vars.PepperedLog)("disabled trace");
  a.Tick("A_1");a.Action_exit();
  Check(a.Logs.Count==priorPrints&&priorBytes.SequenceEqual(File.ReadAllBytes(path))
   &&File.GetLastWriteTimeUtc(path)==priorMtime&&a.Starts==0&&a.Resets==0&&a.Splits==0,
   "OFF after ON causes no append or DebugView print and no timer action");
  a.settings["diagnostics"]=true;a.Tick("A_1");
  ((Action<string>)a.vars.PepperedLog)("re-enabled\r\ntrace");
  Check(HasLog(a,"re-enabled  trace")&&File.ReadAllText(path).Contains("re-enabled  trace")
   &&a.Logs.Count(s=>s.Contains("boot 0.4.0-rc14"))==1
   &&a.Logs.Count(s=>s.Contains("loaded 0.4.0-rc14"))==1
   &&a.Starts==0&&a.Resets==0&&a.Splits==0,
   "re-enabled sink works without repeating banner or changing timer outcomes");
  Console.WriteLine("LOGGING_PRIVACY emitted sink OFF/ON/OFF/ON, private file bytes+mtime and print checked");
 }
 static void LegacyTvBoundaryRegression(bool expectImageRefresh){
  foreach(string setting in new[]{"ending.5.with","ending.6.with","ending.7.with"}){
   var a=New();a.settings["autoStart"]=false;a.settings["splitWorlds"]=false;
   a.settings["splitScenes"]=false;a.settings[setting]=true;
   a.vars.MonoMetadata=new FakeMonoMetadata();a.ManualStart();
   ((FakeReader)a.vars.Reader).Sample.EndingSignals.Add("ending.karmina_tv");
   a.Tick("B_Aftermath");
   bool tvStartSetting=setting=="ending.5.with"||setting=="ending.6.with"||setting=="ending.7.with";
   Check(a.Splits==0&&((FakeMonoMetadata)a.vars.MonoMetadata).Images.ClearCalls==(tvStartSetting&&expectImageRefresh?1:0)
    &&(string)a.vars.LastTvStartMetadataScene=="",
    "legacy TV signal is inert; only a With5/6/7 TV target may retry metadata for "+setting);
  }
  Console.WriteLine("LEGACY_TV_CASES old_signal_no_split=3 targeted_retries=3 untargeted_retry=0");
 }
 static void ActualImageCacheLifecycleRegression(bool expectRefresh){
  var stale=New();stale.settings["autoStart"]=false;stale.settings["splitWorlds"]=false;stale.settings["splitScenes"]=false;stale.settings["ending.10"]=true;
  var fixture=ActualReaderFixture.Create();var oldReader=new ActualReaderFacade(fixture.Initial);
  var metadata=new FakeMonoMetadata();var code=new FakeCode();
  code.CandidateFactory=delegate(){return new ActualReaderFacade(fixture);};
  stale.vars.Code=code;stale.vars.Reader=oldReader;stale.vars.ReaderConfigured=true;
  stale.vars.SupportedBuild=true;stale.vars.MonoMetadata=metadata;stale.ManualStart();fixture.SetBattle();
  stale.Tick("A_1");
  Check(metadata.Images.ClearCalls==0,"non-boundary scene does not refresh image wrappers");
  stale.Tick("T_Boss");
  Check(metadata.Images.ClearCalls==(expectRefresh?1:0),"boundary refresh behavior is explicit");
  ReadResult observed=((ActualReaderFacade)stale.vars.Reader).Read("T_Boss");
  if(expectRefresh){
   Check(ReferenceEquals((object)stale.vars.Reader,code.LastCandidate),"fresh actual candidate is adopted");
   Check(observed.Valid&&observed.BoundaryMetadataReady&&observed.GolfBattleActive==true
    &&observed.GolfBatteries==2&&observed.GolfCutscenePlaying==false,
    "fresh actual candidate exposes qualified battle output");
  }else{
   Check(ReferenceEquals((object)stale.vars.Reader,(object)oldReader),"pre-refresh control keeps stale reader when cache is unchanged");
   Check(observed.Valid&&!observed.BoundaryMetadataReady&&!observed.GolfBattleActive.HasValue
    &&!observed.GolfBatteries.HasValue,"pre-refresh stale reader cannot expose boundary output");
  }
  int healthyClearCount=metadata.Images.ClearCalls;stale.Tick("T_Boss");
  Check(metadata.Images.ClearCalls==healthyClearCount,"healthy LastMetadataScene does not re-refresh");

  var retry=New();retry.settings["autoStart"]=false;retry.settings["splitWorlds"]=false;retry.settings["splitScenes"]=false;retry.settings["ending.10"]=true;
  var retryFixture=ActualReaderFixture.Create();var retryOld=new ActualReaderFacade(retryFixture.Initial);
  var retryMetadata=new FakeMonoMetadata();int attempts=0;var retryCode=new FakeCode();
  retryCode.CandidateFactory=delegate(){attempts++;return attempts==1
   ? (object)new ActualReaderFacade(retryFixture.Initial) : new ActualReaderFacade(retryFixture);};
  retry.vars.Code=retryCode;retry.vars.Reader=retryOld;retry.vars.ReaderConfigured=true;
  retry.vars.SupportedBuild=true;retry.vars.MonoMetadata=retryMetadata;retry.ManualStart();retryFixture.SetBattle();
  retry.Tick("T_Boss");
  Check(attempts==1&&retryMetadata.Images.ClearCalls==(expectRefresh?1:0),"boundary candidate failure is attempted once");
  Check(ReferenceEquals((object)retry.vars.Reader,(object)retryOld),"BoundaryMetadataReady=false retains usable reader");
  int throttledClearCount=retryMetadata.Images.ClearCalls;retry.Tick("T_Boss");
  Check(retryMetadata.Images.ClearCalls==throttledClearCount,"two-second metadata throttle suppresses immediate retry");
  retry.vars.OptionalMetadataRetryAfter=0L;retry.Tick("T_Boss");
  Check(attempts==2&&retryMetadata.Images.ClearCalls==(expectRefresh?2:0),"metadata recovery retries after deadline");
  ReadResult recovered=((ActualReaderFacade)retry.vars.Reader).Read("T_Boss");
  Check((expectRefresh && recovered.BoundaryMetadataReady&&ReferenceEquals((object)retry.vars.Reader,retryCode.LastCandidate))
   || (!expectRefresh && ReferenceEquals((object)retry.vars.Reader,(object)retryOld)),
   "only refreshed actual cache recovers boundary metadata");

  var guarded=New();guarded.settings["splitWorlds"]=false;guarded.settings["splitScenes"]=false;guarded.settings["ending.1"]=true;
  FreshStart(guarded);var guardedReader=(FakeReader)guarded.vars.Reader;guardedReader.Sample.EndingSignals.Add("ending.execution_black");
  guarded.Tick("Boundary");Check(guarded.Splits==1,"pre-failure ending latch is armed");
  var guardedMetadata=new FakeMonoMetadata();guardedMetadata.Images.ThrowOnClear=true;
  guarded.vars.MonoMetadata=guardedMetadata;guarded.vars.LastMetadataScene="";guarded.vars.Code=new FakeCode{Candidate=guardedReader};
  guarded.Tick("T_Boss");
  Check(guardedMetadata.Images.ClearCalls==(expectRefresh?1:0),"clear invocation is observable on failure path");
  Check(ReferenceEquals((object)guarded.vars.Reader,(object)guardedReader)&&guarded.Splits==1
   &&guarded.timer.CurrentPhase==TimerPhase.Running,"clear failure preserves reader and emits no timer action");
  if(expectRefresh) Check(HasLog(guarded,"read failed: InvalidOperationException"),"clear failure is fail-closed through outer update catch");
  guardedMetadata.Images.ThrowOnClear=false;guarded.vars.OptionalMetadataRetryAfter=0L;guarded.Tick("T_Boss");
  Check(guarded.Splits==1,"recovery does not poll an already-latched ending");
 }

 static void ActualLateMetadataRegression(bool expectImageRefresh){
  var late=New();late.settings["enableTimers"]=true;late.settings["autoStart"]=false;
  late.settings["splitWorlds"]=false;late.settings["splitScenes"]=false;late.settings["ending.10"]=true;
  var fixture=ActualReaderFixture.Create();var initial=new ActualReaderFacade(fixture.Initial);
  var code=new FakeCode();int candidateAttempts=0;
  code.CandidateFactory=delegate(){candidateAttempts++;return new ActualReaderFacade(
   candidateAttempts==1?fixture.Invalid:fixture.Full);};
  late.vars.Code=code;late.vars.Reader=initial;late.vars.ReaderConfigured=true;
  var lateMetadata=new FakeMonoMetadata();
  late.vars.SupportedBuild=true;late.vars.MonoMetadata=lateMetadata;late.vars.LastMetadataScene="";
  late.vars.OptionalMetadataRetryAfter=0L;late.vars.MetadataRetryAfter=0L;
  ReadResult cold=fixture.Initial.Read("T_Boss");
  Check(cold.Valid&&!cold.GolfBattleActive.HasValue&&!cold.GolfBatteries.HasValue
   &&!cold.GolfCutscenePlaying.HasValue&&cold.EndingSignals.Count==0,
   "cold active reader is valid without optional choices");
  // Manual start is deliberately before the first valid battle observation.
  late.ManualStart();late.Tick("T_Boss");
  Check(candidateAttempts==1,"cold T_Boss tries metadata candidate");
  Check(lateMetadata.Images.ClearCalls==(expectImageRefresh?1:0),"first metadata retry follows refresh policy");
  Check(ReferenceEquals((object)late.vars.Reader,(object)initial),"invalid candidate keeps usable reader");
  Check((long)late.vars.OptionalMetadataRetryAfter>0L,"invalid candidate is throttled");
  Check(HasLog(late,"golfBatteries=unknown")&&HasLog(late,"golfCutscene=unknown"),
   "cold trace logs nullable golf metadata");
  late.vars.OptionalMetadataRetryAfter=0L;late.Tick("T_Boss");
  Check(candidateAttempts==2,"forced deadline retries late metadata candidate");
  Check(lateMetadata.Images.ClearCalls==(expectImageRefresh?2:0),"forced deadline refresh count is bounded");
  ReadResult battle=fixture.Full.Read("T_Boss");
  Check(battle.Valid&&battle.GolfBattleActive==true&&battle.GolfBatteries==0
   &&battle.GolfCutscenePlaying==false&&battle.EndingSignals.Count==0
   &&battle.TheodoreMetadataReady&&battle.TheodoreHp==100,
   "actual T_Boss zero-battery battle exposes HP without a death signal");
  Check(battle.BoundaryMetadataReady,"actual T_Boss candidate exposes readiness");
  Check(ReferenceEquals((object)late.vars.Reader,code.LastCandidate),"ready candidate is adopted");
  Check(HasLog(late,"golfBatteries=0")&&HasLog(late,"golfCutscene=False"),
   "adopted trace logs zero batteries and non-cutscene state");
  Check(HasLog(late,"theodoreReady=True")&&HasLog(late,"theodoreHp=100"),
   "adopted trace logs qualified Theodore metadata and HP");
  Check(late.Splits==0,"zero-battery T_Boss observation does not finish");
  fixture.SetWin();
  ReadResult win=fixture.Full.Read("T_Boss");
  Check(win.Valid&&win.GolfBattleActive==false&&win.GolfBatteries==0
   &&win.GolfCutscenePlaying==true&&win.TheodoreHp==0
   &&win.EndingSignals.Contains("ending.theodore_victory"),"immediate zero-battery Theodore win is observable");
  fixture.SetInvalid();late.Tick("T_Boss");Check(late.Splits==0,"invalid gap emits no finish");
  fixture.SetLoss();
  ReadResult loss=fixture.Full.Read("T_Boss");
  Check(loss.Valid&&loss.GolfBattleActive==false&&loss.GolfBatteries==-1
   &&loss.GolfCutscenePlaying==true&&loss.TheodoreHp==50
   &&loss.EndingSignals.Contains("ending.theodore_death")
   &&!loss.EndingSignals.Contains("ending.theodore_victory"),
   "actual T_Boss loss emits negative battery reader signal");
  late.Tick("T_Boss");
  Check(HasLog(late,"golfBatteries=-1")&&HasLog(late,"golfCutscene=True"),
   "loss trace logs decremented batteries and cutscene state");
  Check(HasLog(late,"theodoreHp=50"),
   "loss trace keeps the observed positive Theodore HP");
  Check(late.Splits==1,"actual T_Boss loss emits Ending10");
  late.Tick("T_Boss");Check(late.Splits==1,"actual Ending10 is one-shot");
  late.vars.TimerModel.Reset();Check(late.timer.CurrentPhase==TimerPhase.NotRunning,"manual reset ends first trial");
  fixture.SetBattle();late.Tick("[Main Menu]");late.ManualStart();late.Tick("T_Boss");
  ReadResult positive=fixture.Full.Read("T_Boss");
  Check(positive.GolfBattleActive==true&&positive.GolfBatteries==2
   &&positive.GolfCutscenePlaying==false,"positive-battery T_Boss battle remains covered");
  fixture.SetLoss();late.Tick("T_Boss");
  Check(late.Splits==2,"manual reset permits a fresh Theodore loss");

  var initialDeath=New();initialDeath.settings["autoStart"]=false;
  initialDeath.settings["splitWorlds"]=false;initialDeath.settings["splitScenes"]=false;initialDeath.settings["ending.10"]=true;
  var initialDeathFixture=ActualReaderFixture.Create();initialDeath.vars.Code=new FakeCode();
  initialDeath.vars.Reader=new ActualReaderFacade(initialDeathFixture.Full);initialDeath.vars.ReaderConfigured=true;
  initialDeath.vars.SupportedBuild=true;initialDeath.vars.MonoMetadata=null;initialDeath.ManualStart();
  initialDeathFixture.SetWin();initialDeath.Tick("T_Boss");
  Check(initialDeath.Splits==0,"initial cutscene zero-battery candidate has no active loss window");

  var zero=New();zero.settings["autoStart"]=false;zero.settings["splitWorlds"]=false;
  zero.settings["splitScenes"]=false;zero.settings["ending.10"]=true;
  var zeroFixture=ActualReaderFixture.Create();zero.vars.Code=new FakeCode();
  zero.vars.Reader=new ActualReaderFacade(zeroFixture.Full);zero.vars.ReaderConfigured=true;
  zero.vars.SupportedBuild=true;zero.vars.MonoMetadata=null;zero.ManualStart();
  zeroFixture.SetZeroBatteryBattle();zero.Tick("T_Boss");
  zeroFixture.SetWin();zero.Tick("T_Boss");
  Check(zero.Splits==0,"zero-battery active-to-unchanged win is rejected");

  var negative=New();negative.settings["autoStart"]=false;negative.settings["splitWorlds"]=false;
  negative.settings["splitScenes"]=false;negative.settings["ending.10"]=true;
  var negativeFixture=ActualReaderFixture.Create();negative.vars.Code=new FakeCode();
  negative.vars.Reader=new ActualReaderFacade(negativeFixture.Full);negative.vars.ReaderConfigured=true;
  negative.vars.SupportedBuild=true;negative.vars.MonoMetadata=null;negative.ManualStart();
  negativeFixture.SetNegativeBattle();negative.Tick("T_Boss");
  negativeFixture.SetNegativeLoss();
  ReadResult negativeLoss=negativeFixture.Full.Read("T_Boss");
  Check(negativeLoss.GolfBatteries==-3&&negativeLoss.GolfCutscenePlaying==true
   &&negativeLoss.EndingSignals.Contains("ending.theodore_death"),"negative active loss preserves raw reader state");
  negative.Tick("T_Boss");Check(negative.Splits==1,"negative-battery loss finishes below high-water mark");

  var highwater=New();highwater.settings["autoStart"]=false;highwater.settings["splitWorlds"]=false;
  highwater.settings["splitScenes"]=false;highwater.settings["ending.10"]=true;
  var highwaterFixture=ActualReaderFixture.Create();highwater.vars.Code=new FakeCode();
  highwater.vars.Reader=new ActualReaderFacade(highwaterFixture.Full);highwater.vars.ReaderConfigured=true;
  highwater.vars.SupportedBuild=true;highwater.vars.MonoMetadata=null;highwater.ManualStart();
  highwaterFixture.SetZeroBatteryBattle();highwater.Tick("T_Boss");
  highwaterFixture.SetLossBeforeCutscene();highwater.Tick("T_Boss");
  highwaterFixture.SetGolf(true,-1);highwater.Tick("T_Boss");
  Check(highwater.Splits==1,"battery decrement before cutscene uses retained high-water mark");

  var intro=New();intro.settings["enableTimers"]=true;intro.settings["autoStart"]=false;
  intro.settings["splitWorlds"]=false;intro.settings["splitScenes"]=false;intro.settings["ending.10"]=true;
  var introFixture=ActualReaderFixture.Create();intro.vars.Code=new FakeCode();
  intro.vars.Reader=new ActualReaderFacade(introFixture.Full);intro.vars.ReaderConfigured=true;
  intro.vars.SupportedBuild=true;intro.vars.MonoMetadata=null;intro.ManualStart();
  introFixture.SetLoss();intro.Tick("T_Boss");
  Check(intro.Splits==0,"manual start already at death does not retrofire");
 }
 static Actions NewActualSurrender(ReadOnlyReader reader){
  var a=New();a.settings["autoStart"]=false;a.settings["splitWorlds"]=false;
  a.settings["splitScenes"]=false;a.settings["ending.8"]=true;
  a.vars.Reader=new ActualReaderFacade(reader);a.vars.ReaderConfigured=true;
  a.vars.SupportedBuild=true;a.vars.MonoMetadata=null;
  return a;
 }
 static Actions NewActualMerdeka(ActualReaderFixture fixture,string ending){
  var a=New();a.settings["autoStart"]=false;a.settings["splitWorlds"]=false;
  a.settings["splitScenes"]=false;a.settings[ending]=true;
  a.vars.Reader=new ActualReaderFacade(fixture.Initial);a.vars.ReaderConfigured=true;
  a.vars.SupportedBuild=true;a.vars.MonoMetadata=null;
  return a;
 }
 static Actions NewActualTvStart(ActualReaderFixture fixture,string ending){
  return NewActualTvStartReader(fixture.TvOnly,ending);
 }
 static Actions NewActualTvStartReader(ReadOnlyReader reader,string ending){
  var a=New();a.settings["autoStart"]=false;a.settings["splitWorlds"]=false;
  a.settings["splitScenes"]=false;a.settings[ending]=true;
  a.vars.Reader=new ActualReaderFacade(reader);a.vars.ReaderConfigured=true;
  a.vars.SupportedBuild=true;a.vars.MonoMetadata=null;
  return a;
 }
 static Actions NewActualKarmina(ReadOnlyReader reader,string ending){
  var a=New();a.settings["autoStart"]=false;a.settings["splitWorlds"]=false;
  a.settings["splitScenes"]=false;a.settings[ending]=true;
  a.vars.Reader=new ActualReaderFacade(reader);a.vars.ReaderConfigured=true;
  a.vars.SupportedBuild=true;a.vars.MonoMetadata=null;
  return a;
 }
 static void ActualMerdekaOutcomeRegression(){
  // The Merdeka outcome reader uses only the already-qualified Abyss_State
  // integer. This fixture intentionally has no Choices, stars, dialogue, or
  // boundary metadata layout.
  var fixture=ActualReaderFixture.Create();fixture.SetMerdekaStage(49);
  ReadResult armed=fixture.Initial.Read("B_Merdeka");
  Check(armed.Valid&&armed.MerdekaStage==49&&armed.EndingSignals.Count==0,
   "actual Merdeka reader arms on stable stage 49 without optional metadata");
  var stage50Fixture=ActualReaderFixture.Create();stage50Fixture.SetMerdekaStage(49);
  stage50Fixture.Initial.Read("B_Merdeka");stage50Fixture.SetMerdekaStage(50);
  ReadResult invalid=stage50Fixture.Initial.Read("B_Merdeka");
  Check(invalid.Valid&&invalid.MerdekaStage==50
   &&!invalid.EndingSignals.Contains("ending.merdeka_victory")
   &&!invalid.EndingSignals.Contains("ending.merdeka_defeat"),
   "stage 50 is not a Merdeka outcome");
  var victoryFixture=ActualReaderFixture.Create();victoryFixture.SetMerdekaStage(49);
  victoryFixture.Initial.Read("B_Merdeka");victoryFixture.SetMerdekaStage(52);
  ReadResult victory=victoryFixture.Initial.Read("B_Merdeka");
  Check(victory.Valid&&victory.MerdekaStage==52
   &&victory.EndingSignals.Contains("ending.merdeka_victory")
   &&!victory.EndingSignals.Contains("ending.merdeka_defeat"),
   "stable stage 49 to 52 emits Merdeka victory");
  var defeatFixture=ActualReaderFixture.Create();defeatFixture.SetMerdekaStage(49);
  defeatFixture.Initial.Read("B_Merdeka");defeatFixture.SetMerdekaStage(51);
  ReadResult defeat=defeatFixture.Initial.Read("B_Merdeka");
  Check(defeat.Valid&&defeat.MerdekaStage==51
   &&defeat.EndingSignals.Contains("ending.merdeka_defeat")
   &&!defeat.EndingSignals.Contains("ending.merdeka_victory"),
   "stable stage 49 to 51 emits Merdeka defeat");
  var wrongSceneFixture=ActualReaderFixture.Create();wrongSceneFixture.SetMerdekaStage(49);
  wrongSceneFixture.Initial.Read("B_Merdeka");wrongSceneFixture.SetMerdekaStage(51);
  ReadResult wrongScene=wrongSceneFixture.Initial.Read("B_10");
  Check(wrongScene.Valid&&!wrongScene.MerdekaStage.HasValue
   &&wrongScene.EndingSignals.Count==0,"Merdeka outcome is rejected outside B_Merdeka");

  // A raw Merdeka outcome remains observable in the reader log, but the
  // emitted With5/6/7 presets no longer stop on either boss terminal stage.
  var fiveFixture=ActualReaderFixture.Create();var five=NewActualMerdeka(fiveFixture,"ending.5.with");
  five.ManualStart();fiveFixture.SetMerdekaStage(49);five.Tick("B_Merdeka");
  fiveFixture.SetMerdekaStage(51);five.Tick("B_Merdeka");fiveFixture.SetMerdekaStage(52);five.Tick("B_Merdeka");
  Check(five.Splits==0&&HasLog(five,"merdekaStage=51")&&HasLog(five,"merdekaStage=52"),
   "Ending5 preserves raw Merdeka 51/52 diagnostics without a finish");

  var sixFixture=ActualReaderFixture.Create();var six=NewActualMerdeka(sixFixture,"ending.6.with");
  six.ManualStart();sixFixture.SetMerdekaStage(49);six.Tick("B_Merdeka");
  sixFixture.SetMerdekaStage(51);six.Tick("B_Merdeka");sixFixture.SetMerdekaStage(52);six.Tick("B_Merdeka");
  Check(six.Splits==0&&HasLog(six,"merdekaStage=51")&&HasLog(six,"merdekaStage=52"),
   "Ending6 preserves raw Merdeka 51/52 diagnostics without a finish");

  var bothFixture=ActualReaderFixture.Create();var both=NewActualMerdeka(bothFixture,"ending.5.with");
  both.settings["ending.6.with"]=true;both.settings["ending.7.with"]=true;both.ManualStart();
  bothFixture.SetMerdekaStage(49);both.Tick("B_Merdeka");
  bothFixture.SetMerdekaStage(51);both.Tick("B_Merdeka");bothFixture.SetMerdekaStage(52);both.Tick("B_Merdeka");
  Check(both.Splits==0&&HasLog(both,"merdekaStage=51")&&HasLog(both,"merdekaStage=52"),
   "all three With5/6/7 presets preserve raw Merdeka diagnostics without a finish");

  var fiftyActionFixture=ActualReaderFixture.Create();var fiftyAction=NewActualMerdeka(fiftyActionFixture,"ending.5.with");
  fiftyAction.ManualStart();fiftyActionFixture.SetMerdekaStage(49);fiftyAction.Tick("B_Merdeka");
  fiftyActionFixture.SetMerdekaStage(50);fiftyAction.Tick("B_Merdeka");
  Check(fiftyAction.Splits==0,"emitted ASL does not treat stage 50 as a Merdeka outcome");
  fiftyActionFixture.SetMerdekaStage(52);fiftyAction.Tick("B_Merdeka");
  Check(fiftyAction.Splits==0,"stage 50 gap does not turn the old victory into a With5 finish");

  // A first observed terminal stage is not retroactive. Even after a later
  // 49 -> 51 arm, Ending7 still waits for the qualified TV event.
  var firstTerminalFixture=ActualReaderFixture.Create();var firstTerminal=NewActualMerdeka(firstTerminalFixture,"ending.7.with");
  firstTerminal.ManualStart();firstTerminalFixture.SetMerdekaStage(51);firstTerminal.Tick("B_Merdeka");
  Check(firstTerminal.Splits==0,"first observed Merdeka terminal stage does not retrofire");
  firstTerminalFixture.SetMerdekaStage(49);firstTerminal.Tick("B_Merdeka");
  firstTerminalFixture.SetMerdekaStage(51);firstTerminal.Tick("B_Merdeka");
  Check(firstTerminal.Splits==0&&HasLog(firstTerminal,"merdekaStage=51"),
   "later raw defeat arm still does not finish Ending7");

  // The reader retains a qualified stage-49 arm over a transient invalid
  // sample, but explicit reset clears it. Neither path may finish Ending7.
  var gapFixture=ActualReaderFixture.Create();var gap=NewActualMerdeka(gapFixture,"ending.7.with");
  gap.ManualStart();gapFixture.SetMerdekaStage(49);gap.Tick("B_Merdeka");gapFixture.SetInvalid();gap.Tick("B_Merdeka");
  gapFixture.SetMerdekaStage(51);gap.Tick("B_Merdeka");
  Check(gap.Splits==0&&HasLog(gap,"merdekaStage=51"),
   "same-attempt invalid gap retains only raw defeat diagnostics");

  var resetFixture=ActualReaderFixture.Create();var reset=NewActualMerdeka(resetFixture,"ending.7.with");
  var resetReader=(ActualReaderFacade)reset.vars.Reader;reset.ManualStart();resetFixture.SetMerdekaStage(49);
  reset.Tick("B_Merdeka");reset.vars.TimerModel.Reset();reset.ManualStart();resetFixture.SetMerdekaStage(51);reset.Tick("B_Merdeka");
  Check(reset.Splits==0&&resetReader.MerdekaBindingResetCalls>=2&&HasLog(reset,"merdekaStage=51"),
   "onReset clears Merdeka arm without finishing Ending7");
  reset.ManualStart();resetFixture.SetMerdekaStage(49);reset.Tick("B_Merdeka");
  resetFixture.SetMerdekaStage(51);reset.Tick("B_Merdeka");
  Check(reset.Splits==0&&HasLog(reset,"merdekaStage=51"),
   "post-reset raw defeat can rearm diagnostics but not Ending7");

  var identityFixture=ActualReaderFixture.Create();var identity=NewActualMerdeka(identityFixture,"ending.7.with");
  var identityReader=(ActualReaderFacade)identity.vars.Reader;identity.ManualStart();
  ((FakeHelper)identity.vars.Helper).Scenes.Active.Address=new IntPtr(0x1111);
  identityFixture.SetMerdekaStage(49);identity.Tick("B_Merdeka");
  int identityResetsBeforeAddressChange=identityReader.MerdekaBindingResetCalls;
  ((FakeHelper)identity.vars.Helper).Scenes.Active.Address=new IntPtr(0x2222);
  identityFixture.SetMerdekaStage(51);identity.Tick("B_Merdeka");
  Check(identity.Splits==0&&identityReader.MerdekaBindingResetCalls>identityResetsBeforeAddressChange
   &&HasLog(identity,"merdekaStage=51"),
   "same-named new-scene address clears stale raw defeat state without Ending7 finish");
  identityFixture.SetMerdekaStage(49);identity.Tick("B_Merdeka");identityFixture.SetMerdekaStage(51);identity.Tick("B_Merdeka");
  Check(identity.Splits==0&&HasLog(identity,"merdekaStage=51"),
   "new-scene Merdeka diagnostic can rearm without finishing Ending7");
  Console.WriteLine("MERDEKA_CASES actual_reader=1 with5_boss_guard=1 with6_boss_guard=1 with7_boss_guard=1 with5+6+7_boss_guard=1 stage50_guard=2 first_terminal_guard=1 invalid_gap=1 reset=1 address_boundary=1 no_optional_metadata=1");
 }
 static void OptionalFriendLossRegression(){
  const string optional="ending.8.friend_loss";
  // Initial is the real reader with only Abyss_State: no chooser, stars,
  // Choices dictionary, TMP, friendship flag, or optional dialogue layout.
  var fixture=ActualReaderFixture.Create();var a=NewActualMerdeka(fixture,optional);
  a.ManualStart();fixture.SetMerdekaStage(49);a.Tick("B_Merdeka");
  Check(a.Splits==0,"optional Ending8 arms without optional metadata");
  fixture.SetMerdekaStage(51);a.Tick("B_Merdeka");
  Check(a.Splits==1&&HasLog(a,"split=ending.merdeka_defeat")
   &&(string)a.vars.LastSurrenderMetadataScene=="",
   "optional Ending8 splits at actual fresh 49->51 without surrender/route metadata");
  a.Tick("B_Merdeka");Check(a.Splits==1,"optional Ending8 loss one shot");

  var legacyFixture=ActualReaderFixture.Create();var legacy=NewActualMerdeka(legacyFixture,"ending.7.with");
  // An old layout has no new checkbox override: startup's default is false.
  Check(!legacy.settings[optional],"legacy layout leaves new checkbox default off");
  legacy.ManualStart();legacyFixture.SetMerdekaStage(49);legacy.Tick("B_Merdeka");
  legacyFixture.SetMerdekaStage(51);legacy.Tick("B_Merdeka");
  Check(legacy.Splits==0,"old Ending7 layout cannot be retimed by new loss setting");

  var offFixture=ActualReaderFixture.Create();var off=NewActualMerdeka(offFixture,optional);
  off.settings[optional]=false;off.ManualStart();offFixture.SetMerdekaStage(49);off.Tick("B_Merdeka");
  offFixture.SetMerdekaStage(51);off.Tick("B_Merdeka");
  Check(off.Splits==0,"unchecked optional Ending8 loss does not split");
  off.settings[optional]=true;off.Tick("B_Merdeka");
  Check(off.Splits==0,"hot-enabling cannot replay a consumed loss");
  var masterFixture=ActualReaderFixture.Create();var master=NewActualMerdeka(masterFixture,optional);
  master.settings["splitEndings"]=false;master.ManualStart();
  masterFixture.SetMerdekaStage(49);master.Tick("B_Merdeka");
  masterFixture.SetMerdekaStage(51);master.Tick("B_Merdeka");
  Check(master.Splits==0,"disabled endings parent prevents optional loss split");

  var winFixture=ActualReaderFixture.Create();var win=NewActualMerdeka(winFixture,optional);
  win.ManualStart();winFixture.SetMerdekaStage(49);win.Tick("B_Merdeka");
  winFixture.SetMerdekaStage(52);win.Tick("B_Merdeka");
  Check(win.Splits==0,"fresh victory 52 never finishes optional loss");
  var fiftyFixture=ActualReaderFixture.Create();var fifty=NewActualMerdeka(fiftyFixture,optional);
  fifty.ManualStart();fiftyFixture.SetMerdekaStage(49);fifty.Tick("B_Merdeka");
  fiftyFixture.SetMerdekaStage(50);fifty.Tick("B_Merdeka");
  Check(fifty.Splits==0,"stage 50 cannot finish optional loss");
  var wrongFixture=ActualReaderFixture.Create();var wrong=NewActualMerdeka(wrongFixture,optional);
  wrong.ManualStart();wrongFixture.SetMerdekaStage(49);wrong.Tick("B_Merdeka");
  wrongFixture.SetMerdekaStage(51);wrong.Tick("B_10");
  Check(wrong.Splits==0,"stage 51 in B_10 cannot finish optional loss");
  wrong.Tick("B_Merdeka");
  Check(wrong.Splits==0,"scene exit clears arm even when terminal stage returns");

  var firstFixture=ActualReaderFixture.Create();var first=NewActualMerdeka(firstFixture,optional);
  first.ManualStart();firstFixture.SetMerdekaStage(51);first.Tick("B_Merdeka");
  Check(first.Splits==0,"manual start already at stage 51 never retrofires");
  firstFixture.SetMerdekaStage(49);first.Tick("B_Merdeka");
  firstFixture.SetMerdekaStage(51);first.Tick("B_Merdeka");
  Check(first.Splits==1,"later fresh 49->51 can finish optional Ending8");

  var gapFixture=ActualReaderFixture.Create();var gap=NewActualMerdeka(gapFixture,optional);
  gap.ManualStart();gapFixture.SetMerdekaStage(49);gap.Tick("B_Merdeka");
  gapFixture.SetInvalid();gap.Tick("B_Merdeka");
  gapFixture.SetMerdekaStage(51);gap.Tick("B_Merdeka");
  Check(gap.Splits==1,"same-attempt invalid sample retains fresh Merdeka arm");

  var resetFixture=ActualReaderFixture.Create();var reset=NewActualMerdeka(resetFixture,optional);
  var resetReader=(ActualReaderFacade)reset.vars.Reader;
  reset.ManualStart();resetFixture.SetMerdekaStage(49);reset.Tick("B_Merdeka");
  reset.vars.TimerModel.Reset();reset.ManualStart();resetFixture.SetMerdekaStage(51);reset.Tick("B_Merdeka");
  Check(reset.Splits==0&&resetReader.MerdekaBindingResetCalls>=2,
   "reset clears optional Ending8 reader arm");
  resetFixture.SetMerdekaStage(49);reset.Tick("B_Merdeka");
  resetFixture.SetMerdekaStage(51);reset.Tick("B_Merdeka");
  Check(reset.Splits==1,"new run may rearm fresh optional loss");

  var identityFixture=ActualReaderFixture.Create();var identity=NewActualMerdeka(identityFixture,optional);
  identity.ManualStart();((FakeHelper)identity.vars.Helper).Scenes.Active.Address=new IntPtr(0x1111);
  identityFixture.SetMerdekaStage(49);identity.Tick("B_Merdeka");
  ((FakeHelper)identity.vars.Helper).Scenes.Active.Address=new IntPtr(0x2222);
  identityFixture.SetMerdekaStage(51);identity.Tick("B_Merdeka");
  Check(identity.Splits==0,"same-named new scene cannot use old Merdeka arm");
  identityFixture.SetMerdekaStage(49);identity.Tick("B_Merdeka");
  identityFixture.SetMerdekaStage(51);identity.Tick("B_Merdeka");
  Check(identity.Splits==1,"new scene address can arm its own fresh defeat");
  var attachFixture=ActualReaderFixture.Create();var attach=NewActualMerdeka(attachFixture,optional);
  attach.ManualStart();attachFixture.SetMerdekaStage(49);attach.Tick("B_Merdeka");
  attach.settings["autoReset"]=false;attach.Action_exit();attach.vars.SupportedBuild=true;attach.vars.ReaderConfigured=true;
  attachFixture.SetMerdekaStage(51);attach.Tick("B_Merdeka");
  Check(attach.Splits==0,"detach/reattach cannot reuse stale optional loss arm");
  attachFixture.SetMerdekaStage(49);attach.Tick("B_Merdeka");
  attachFixture.SetMerdekaStage(51);attach.Tick("B_Merdeka");
  Check(attach.Splits==1,"reattached run can arm a fresh optional loss; "+String.Join(" | ",attach.Logs.ToArray()));

  var pausedFixture=ActualReaderFixture.Create();var paused=NewActualMerdeka(pausedFixture,optional);
  paused.ManualStart();pausedFixture.SetMerdekaStage(49);paused.Tick("B_Merdeka");
  paused.timer.CurrentPhase=TimerPhase.Paused;pausedFixture.SetMerdekaStage(51);paused.Tick("B_Merdeka");
  paused.timer.CurrentPhase=TimerPhase.Running;paused.Tick("B_Merdeka");
  Check(paused.Splits==0,"paused loss consumed, not replayed");
  var disabledFixture=ActualReaderFixture.Create();var disabled=NewActualMerdeka(disabledFixture,optional);
  disabled.ManualStart();disabledFixture.SetMerdekaStage(49);disabled.Tick("B_Merdeka");
  disabled.settings["enableTimers"]=false;disabledFixture.SetMerdekaStage(51);disabled.Tick("B_Merdeka");
  disabled.settings["enableTimers"]=true;disabled.Tick("B_Merdeka");
  Check(disabled.Splits==0,"disabled timer consumes loss without replay");

  // The older Ending8 confirmation is not a defeat, and remains independent.
  var surrenderFixture=ActualReaderFixture.Create();
  var old8=NewActualSurrender(surrenderFixture.SurrenderOnly);
  old8.ManualStart();surrenderFixture.SetSurrenderPending();old8.Tick("B_Merdeka");
  surrenderFixture.SetSurrenderChoice(1,false);old8.Tick("B_Merdeka");
  Check(old8.Splits==1&&HasLog(old8,"split=ending.merdeka_surrender"),
   "old Ending8 surrender still fires independently");
  Console.WriteLine("FRIEND_LOSS_CASES reader_to_asl=PASS legacy_default_off=PASS lifecycle=PASS old8_isolation=PASS");
 }
 static void ActualTvStartRegression(bool expectImageRefresh){
  // The TV reader is intentionally isolated: no Choices dictionary, TMP
  // visible-character field, rating, stars, or ButtonChooser is supplied.
  var readerFixture=ActualReaderFixture.Create();readerFixture.SetTv(false,0,"B_A/m32");
  ReadResult idle=readerFixture.TvOnly.Read("B_Aftermath");
  Check(idle.Valid&&idle.BoundaryMetadataReady&&idle.TvStartMetadataReady
   &&idle.TvStartActive==false,"minimal TV fixture is metadata-ready but inactive before News");
  readerFixture.SetTv(true,0,"B_A/m36",true);
  ReadResult active=readerFixture.TvOnly.Read("B_Aftermath");
  Check(active.Valid&&active.TvStartMetadataReady&&active.TvStartActive==true,
   "B_A/m36 line zero with Playing true is the qualified TV start");
  Check(!active.GolfBatteries.HasValue&&!active.KarminaChoice.HasValue
   &&!active.SurrenderChoice.HasValue,"TV start needs no rating, chooser, or Ending8 metadata");

  // Endings 5, 6, and 7 all finish at the same qualified TV-start event.
  foreach(string setting in new[]{"ending.5.with","ending.6.with","ending.7.with"}){
   var fixture=ActualReaderFixture.Create();var action=NewActualTvStart(fixture,setting);
   action.ManualStart();fixture.SetTv(false,0,"B_A/m32");action.Tick("B_Aftermath");
   fixture.SetTv(true,0,"B_A/m36");action.Tick("B_Aftermath");
   Check(action.Splits==1&&HasLog(action,"tvStartReady=True")&&HasLog(action,"tvStartActive=True"),
    setting+" emits the TV-start finish through the emitted adapter");
   action.Tick("B_Aftermath");Check(action.Splits==1,setting+" TV start is one-shot");
  }
  var bothFixture=ActualReaderFixture.Create();var both=NewActualTvStart(bothFixture,"ending.5.with");
  both.settings["ending.6.with"]=true;both.settings["ending.7.with"]=true;both.ManualStart();bothFixture.SetTv(false,0,"B_A/m33");
  both.Tick("B_Aftermath");bothFixture.SetTv(true,0,"B_A/m36");both.Tick("B_Aftermath");
  Check(both.Splits==1,"all three With5/6/7 aliases share exactly one TV-start event");

  // A run started while the TV is already active must not retrofire. The
  // full News scene can go straight from camera setup to B_A/m36.
  var already=ActualReaderFixture.Create();already.SetTv(true,0,"B_A/m36");
  var alreadyAction=NewActualTvStart(already,"ending.5.with");alreadyAction.ManualStart();alreadyAction.Tick("B_Aftermath");
  var paused=ActualReaderFixture.Create();var pausedAction=NewActualTvStart(paused,"ending.7.with");
  pausedAction.ManualStart();paused.SetTv(true,0,"B_A/m36");pausedAction.timer.CurrentPhase=TimerPhase.Paused;
  pausedAction.Tick("B_Aftermath");pausedAction.timer.CurrentPhase=TimerPhase.Running;pausedAction.Tick("B_Aftermath");
  Check(alreadyAction.Splits==0&&pausedAction.Splits==0,
   "already-active and paused first TV samples cannot retrofire after a run starts");

  // Scene, line, array, runtime class, and manager-root gates reject malformed
  // samples without consulting any unrelated metadata.
  var wrongScene=ActualReaderFixture.Create();wrongScene.SetTv(true,0,"B_A/m36");
  Check(wrongScene.TvOnly.Read("B_Store").TvStartActive!=true,"TV start is scoped to B_Aftermath");
  var notPlaying=ActualReaderFixture.Create();notPlaying.SetTv(false,0,"B_A/m36");
  Check(notPlaying.TvOnly.Read("B_Aftermath").TvStartActive!=true,"TV start requires Playing");
  var wrongLine=ActualReaderFixture.Create();wrongLine.SetTv(true,0,"B_A/m35");
  Check(wrongLine.TvOnly.Read("B_Aftermath").TvStartActive!=true,"wrong News dialogue line is inert");
  var wrongIndex=ActualReaderFixture.Create();wrongIndex.SetTv(true,1,"B_A/m36");
  Check(wrongIndex.TvOnly.Read("B_Aftermath").TvStartActive!=true,"TV start requires line index zero");
  var wrongLength=ActualReaderFixture.Create();wrongLength.SetTv(true,0,"B_A/m36");wrongLength.SetTvArrayLength(2);
  Check(wrongLength.TvOnly.Read("B_Aftermath").TvStartActive!=true,"TV start requires a one-line array");
  var wrongRoot=ActualReaderFixture.Create();wrongRoot.SetTv(true,0,"B_A/m36");wrongRoot.SetTvRootClass(false);
  Check(wrongRoot.TvOnly.Read("B_Aftermath").TvStartActive!=true,"TV start rejects the wrong dialogue runtime class");
  var wrongManager=ActualReaderFixture.Create();wrongManager.SetTv(true,0,"B_A/m36");wrongManager.SetTvManager(false);
  Check(wrongManager.TvOnly.Read("B_Aftermath").TvStartActive!=true,"TV start rejects a missing manager instance");

  // A coherent invalid gap after a positive core seed cannot manufacture a
  // finish, but a later qualified sample still reaches the same event.
  var gap=ActualReaderFixture.Create();var gapAction=NewActualTvStart(gap,"ending.5.with");gapAction.ManualStart();
  gap.SetTv(false,0,"B_A/m32");gapAction.Tick("B_Aftermath");gap.SetTvManager(false);gapAction.Tick("B_Aftermath");
  Check(gapAction.Splits==0,"invalid TV metadata gap emits no finish");
  gap.SetTvManager(true);gap.SetTv(true,0,"B_A/m36");gapAction.Tick("B_Aftermath");
  Check(gapAction.Splits==1,"qualified TV sample after an invalid gap can finish once");

  // The optional TV reader has its own candidate admission/retry latch.
  var disabled=ActualReaderFixture.Create();var disabledAction=NewActualTvStart(disabled,"ending.5.with");
  disabledAction.settings["ending.5.with"]=false;var disabledCode=new FakeCode();int disabledAttempts=0;
  disabledCode.CandidateFactory=delegate(){disabledAttempts++;return new ActualReaderFacade(disabled.TvOnly);};
  disabledAction.vars.Code=disabledCode;disabledAction.vars.MonoMetadata=new FakeMonoMetadata();disabledAction.ManualStart();
  disabled.SetTv(false,0,"B_A/m32");disabledAction.Tick("B_Aftermath");
  Check(disabledAttempts==0&&((FakeMonoMetadata)disabledAction.vars.MonoMetadata).Images.ClearCalls==0,
   "disabled With5 does not poll or refresh optional TV metadata");

  var recovery=ActualReaderFixture.Create();var recoveryAction=NewActualTvStart(recovery,"ending.7.with");
  var recoveryOld=(ActualReaderFacade)recoveryAction.vars.Reader;var recoveryMeta=new FakeMonoMetadata();int recoveryAttempts=0;
  var recoveryCode=new FakeCode();recoveryCode.CandidateFactory=delegate(){recoveryAttempts++;
   return recoveryAttempts==1?(object)new ActualReaderFacade(recovery.Initial):new ActualReaderFacade(recovery.TvOnly);};
  recoveryAction.vars.Code=recoveryCode;recoveryAction.vars.MonoMetadata=recoveryMeta;recoveryAction.ManualStart();
  recovery.SetTv(false,0,"B_A/m32");recoveryAction.Tick("B_Aftermath");
  Check(recoveryAttempts==1&&recoveryMeta.Images.ClearCalls==(expectImageRefresh?1:0)
   &&ReferenceEquals((object)recoveryAction.vars.Reader,(object)recoveryOld)
   &&(string)recoveryAction.vars.LastTvStartMetadataScene=="",
   "failed TV-ready candidate is not admitted");
  recoveryAction.vars.OptionalMetadataRetryAfter=0L;recoveryAction.Tick("B_Aftermath");
  Check(recoveryAttempts==2&&recoveryMeta.Images.ClearCalls==(expectImageRefresh?2:0)
   &&ReferenceEquals((object)recoveryAction.vars.Reader,recoveryCode.LastCandidate)
   &&(string)recoveryAction.vars.LastTvStartMetadataScene=="B_Aftermath",
   "TV-ready candidate is admitted through the independent retry latch");
  recovery.SetTv(true,0,"B_A/m36");recoveryAction.Tick("B_Aftermath");
  Check(recoveryAction.Splits==1&&HasLog(recoveryAction,"tvStartReady=True"),
   "adopted TV reader reaches the emitted TV-start action");

  var late=ActualReaderFixture.Create();var lateAction=NewActualTvStart(late,"ending.6.with");
  lateAction.settings["ending.6.with"]=false;int lateAttempts=0;var lateCode=new FakeCode();
  lateCode.CandidateFactory=delegate(){lateAttempts++;return new ActualReaderFacade(late.TvOnly);};
  lateAction.vars.Code=lateCode;lateAction.vars.MonoMetadata=new FakeMonoMetadata();lateAction.ManualStart();
  late.SetTv(false,0,"B_A/m32");lateAction.Tick("B_Aftermath");Check(lateAttempts==0,"disabled late-enable With6 does not pre-admit TV metadata");
  lateAction.settings["ending.6.with"]=true;lateAction.vars.OptionalMetadataRetryAfter=0L;lateAction.Tick("B_Aftermath");
  var late7=ActualReaderFixture.Create();var late7Action=NewActualTvStart(late7,"ending.7.with");
  late7Action.settings["ending.7.with"]=false;int late7Attempts=0;var late7Code=new FakeCode();
  late7Code.CandidateFactory=delegate(){late7Attempts++;return new ActualReaderFacade(late7.TvOnly);};
  late7Action.vars.Code=late7Code;late7Action.vars.MonoMetadata=new FakeMonoMetadata();late7Action.ManualStart();
  late7.SetTv(false,0,"B_A/m32");late7Action.Tick("B_Aftermath");Check(late7Attempts==0,"disabled late-enable With7 does not pre-admit TV metadata");
  late7Action.settings["ending.7.with"]=true;late7Action.vars.OptionalMetadataRetryAfter=0L;late7Action.Tick("B_Aftermath");
  Check(lateAttempts==1&&(string)lateAction.vars.LastTvStartMetadataScene=="B_Aftermath"
   &&late7Attempts==1&&(string)late7Action.vars.LastTvStartMetadataScene=="B_Aftermath",
   "late-enabled With6 and With7 retry and record TV metadata independently");

  // End-to-end post-loss route: the same actual reader sees raw Merdeka 49/51,
  // then the B_Aftermath intro with TV inactive, and finally the native TV
  // start. Only the last qualified sample may finish Ending7.
  var postLoss=ActualReaderFixture.Create();
  var postLossAction=NewActualTvStartReader(postLoss.Full,"ending.7.with");postLossAction.ManualStart();
  postLoss.SetMerdekaStage(49);postLossAction.Tick("B_Merdeka");
  postLoss.SetMerdekaStage(51);postLossAction.Tick("B_Merdeka");
  bool rawDefeatNoFinish=postLossAction.Splits==0&&HasLog(postLossAction,"merdekaStage=51");
  postLoss.SetTv(false,0,"B_A/m32");postLossAction.Tick("B_Aftermath");
  bool inactiveTvNoFinish=postLossAction.Splits==0&&HasLog(postLossAction,"tvStartActive=False");
  postLoss.SetTv(true,0,"B_A/m36");postLossAction.Tick("B_Aftermath");
  Check(rawDefeatNoFinish&&inactiveTvNoFinish&&postLossAction.Splits==1
   &&HasLog(postLossAction,"tvStartReady=True")&&HasLog(postLossAction,"tvStartActive=True"),
   "post-loss defeat and inactive intro wait for the native TV start before Ending7");

  Console.WriteLine("TV_START_CASES actual_reader=1 with5=1 with6=1 with7=1 all_aliases_once=1 boss_guard=2 malformed=7 invalid_gap=1 paused=2 retry=1 late_enable=2 post_loss_route=1");
 }
 static void ActualSurrenderChoiceRegression(bool expectImageRefresh){
  var fixture=ActualReaderFixture.Create();fixture.SetSurrenderPending();
  ReadResult pending=fixture.SurrenderOnly.Read("B_Merdeka");
  Check(pending.Valid&&pending.BoundaryMetadataReady&&pending.SurrenderMetadataReady
   &&pending.SurrenderChoice==0,"actual surrender reader qualifies final pending choice");
  fixture.SetSurrenderChoice(1,false);
  ReadResult confirmation=fixture.SurrenderOnly.Read("B_Merdeka");
  Check(confirmation.Valid&&confirmation.SurrenderMetadataReady&&confirmation.SurrenderChoice==1,
   "actual surrender reader accepts final choice after dialogue closes");
  Check(confirmation.EndingSignals.Contains("ending.merdeka_surrender"),
   "actual reader emits the qualified Merdeka surrender signal");

  // The first offer and the retained ButtonChoice=1 on B_M/m45 are not
  // enough. The emitted core must see a qualified pending zero first.
  var early=NewActualSurrender(fixture.SurrenderOnly);early.ManualStart();
  fixture.SetSurrenderChoice(1,true);early.Tick("B_Merdeka");
  fixture.SetSurrenderChoice(1,true);early.Tick("B_Merdeka");
  Check(early.Splits==0,"first offer and retained m45 choice do not finish Ending8");
  var finish=NewActualSurrender(fixture.SurrenderOnly);finish.ManualStart();
  fixture.SetSurrenderPending();finish.Tick("B_Merdeka");
  fixture.SetSurrenderChoice(1,false);finish.Tick("B_Merdeka");
  Check(finish.Splits==1&&HasLog(finish,"surrenderChoice=1"),
   "actual emitted reader choice propagates to the Ending8 split");

  var fight=NewActualSurrender(fixture.SurrenderOnly);fight.ManualStart();
  fixture.SetSurrenderPending();fight.Tick("B_Merdeka");fixture.SetSurrenderChoice(2,false);
  fight.Tick("B_Merdeka");Check(fight.Splits==0,"final fight choice does not finish Ending8");

  // The reader's context gates are source-qualified: Playing, Texting, one
  // line at index zero, and the exact left/right prompt pair.
  fixture.SetSurrender(false,false,0,0,"B_M/m45","B_M/m41","B_M/m42");
  ReadResult notPlaying=fixture.SurrenderOnly.Read("B_Merdeka");
  Check(notPlaying.Valid&&notPlaying.SurrenderMetadataReady
   &&!notPlaying.EndingSignals.Contains("ending.merdeka_surrender"),
   "surrender choice requires the active dialogue window");
  fixture.SetSurrender(true,true,0,0,"B_M/m45","B_M/m41","B_M/m42");
  ReadResult texting=fixture.SurrenderOnly.Read("B_Merdeka");
  Check(texting.Valid&&texting.SurrenderMetadataReady
   &&!texting.EndingSignals.Contains("ending.merdeka_surrender"),
   "surrender choice rejects Texting mode");
  fixture.SetSurrender(true,false,1,0,"B_M/m45","B_M/m41","B_M/m42");
  ReadResult wrongIndex=fixture.SurrenderOnly.Read("B_Merdeka");
  Check(wrongIndex.Valid&&wrongIndex.SurrenderMetadataReady
   &&!wrongIndex.EndingSignals.Contains("ending.merdeka_surrender"),
   "surrender choice requires first line index");
  fixture.Memory.Int(ActualReaderFixture.SurrenderLines+24,2);
  fixture.SetSurrender(true,false,0,0,"B_M/m45","B_M/m41","B_M/m42");
  ReadResult wrongLength=fixture.SurrenderOnly.Read("B_Merdeka");
  Check(wrongLength.Valid&&wrongLength.SurrenderMetadataReady
   &&!wrongLength.EndingSignals.Contains("ending.merdeka_surrender"),
   "surrender choice requires a one-line prompt");
  fixture.Memory.Int(ActualReaderFixture.SurrenderLines+24,1);
  fixture.SetSurrenderPending();

  var identity=NewActualSurrender(fixture.SurrenderOnly);identity.ManualStart();
  fixture.SetSurrenderPending();identity.Tick("B_Merdeka");fixture.SetSurrenderIdentity(true,1);
  identity.Tick("B_Merdeka");
  Check(identity.Splits==0,"dialogue/chooser identity change rejects stale surrender confirmation");
  fixture.SetSurrenderIdentity(false,0);identity.Tick("B_Merdeka");
  fixture.SetSurrenderChoice(1,false);identity.Tick("B_Merdeka");
  Check(identity.Splits==1,"same qualified dialogue/chooser identity can rearm Ending8");

  // A transient invalid read in the same attempt retains only a qualified
  // pending zero. Explicit reset clears that local reader binding.
  var gap=NewActualSurrender(fixture.SurrenderOnly);var gapReader=(ActualReaderFacade)gap.vars.Reader;
  gap.ManualStart();fixture.SetSurrenderPending();gap.Tick("B_Merdeka");fixture.SetInvalid();gap.Tick("B_Merdeka");
  fixture.SetSurrenderChoice(1,false);gap.Tick("B_Merdeka");
  Check(gap.Splits==1,"same-attempt invalid gap retains qualified surrender pending state");
  var reset=NewActualSurrender(fixture.SurrenderOnly);var resetReader=(ActualReaderFacade)reset.vars.Reader;
  reset.ManualStart();fixture.SetSurrenderPending();reset.Tick("B_Merdeka");reset.vars.TimerModel.Reset();
  fixture.SetSurrenderChoice(1,false);reset.Tick("B_Merdeka");
  Check(reset.Splits==0&&resetReader.SurrenderBindingResetCalls>=2,
   "onReset clears surrender binding without retrofiring final choice");

  var disabled=NewActualSurrender(fixture.Initial);disabled.settings["ending.8"]=false;
  var disabledCode=new FakeCode();int disabledAttempts=0;
  disabledCode.CandidateFactory=delegate(){disabledAttempts++;return new ActualReaderFacade(fixture.SurrenderOnly);};
  disabled.vars.Code=disabledCode;disabled.vars.MonoMetadata=new FakeMonoMetadata();disabled.ManualStart();
  fixture.SetSurrenderPending();disabled.Tick("B_Merdeka");
  Check(disabledAttempts==0&&((FakeMonoMetadata)disabled.vars.MonoMetadata).Images.ClearCalls==0
   &&disabled.Splits==0,"disabled Ending8 does not admit or poll surrender metadata");

  // This candidate has only the surrender layout: no Choices, Collectibles,
  // Theodore, friendship flag, or StarNum is consulted for Ending8 admission.
  var recovery=NewActualSurrender(fixture.Initial);var recoveryOld=(ActualReaderFacade)recovery.vars.Reader;
  var metadata=new FakeMonoMetadata();
  var code=new FakeCode();int attempts=0;
  code.CandidateFactory=delegate(){attempts++;return attempts==1
   ? (object)new ActualReaderFacade(fixture.Initial)
   : new ActualReaderFacade(fixture.SurrenderOnly);};
  recovery.vars.Code=code;recovery.vars.ReaderConfigured=true;recovery.vars.MonoMetadata=metadata;
  recovery.ManualStart();fixture.SetSurrenderPending();recovery.Tick("B_Merdeka");
  Check(attempts==1&&metadata.Images.ClearCalls==(expectImageRefresh?1:0),
   "Ending8 metadata candidate is attempted through emitted ASL");
  Check(ReferenceEquals((object)recovery.vars.Reader,(object)recoveryOld)
   &&(string)recovery.vars.LastSurrenderMetadataScene=="",
   "failed isolated surrender candidate is not admitted");
  recovery.vars.OptionalMetadataRetryAfter=0L;recovery.Tick("B_Merdeka");
  Check(attempts==2&&metadata.Images.ClearCalls==(expectImageRefresh?2:0)
   &&ReferenceEquals((object)recovery.vars.Reader,code.LastCandidate)
   &&(string)recovery.vars.LastSurrenderMetadataScene=="B_Merdeka",
   "qualified isolated surrender metadata is adopted independently");
  ReadResult recovered=((ActualReaderFacade)recovery.vars.Reader).Read("B_Merdeka");
  Check(recovered.SurrenderMetadataReady&&recovered.SurrenderChoice==0,
   "adopted isolated reader emits pending surrender choice");
  fixture.SetSurrenderChoice(1,false);recovery.Tick("B_Merdeka");
  Check(recovery.Splits==1&&HasLog(recovery,"surrenderReady=True")
   &&HasLog(recovery,"surrenderChoice=1"),
   "isolated reader's final choice reaches emitted timer action");
  Console.WriteLine("SURRENDER_CASES actual_reader=1 first_offer_guard=1 fight_guard=1 isolated_retry=1");
 }
 static void ActualKarminaFinalChoiceRegression(bool expectImageRefresh){
  string[] aliases=new[]{"ending.5.against","ending.6.against","ending.7.against"};

  // The final B_End prompt is read through the real bounded reader. It uses
  // the existing qualified dialogue/chooser layout, but has a separate arm.
  var readerFixture=ActualReaderFixture.Create();readerFixture.SetKarminaPending();
  ReadResult pending=readerFixture.KarminaOnly.Read("B_End");
  Check(pending.Valid&&pending.BoundaryMetadataReady&&pending.KarminaMetadataReady
   &&pending.KarminaChoice==0&&pending.EndingSignals.Count==0,
   "actual Karmina reader arms final B_End prompt on qualified zero");
  readerFixture.SetKarminaChoice(1,false);ReadResult continueChoice=readerFixture.KarminaOnly.Read("B_End");
  Check(continueChoice.Valid&&continueChoice.KarminaChoice==1
   &&continueChoice.EndingSignals.Contains("ending.karmina_final_choice"),
   "actual Karmina reader accepts final Continue choice after zero arm");

  var giveUpFixture=ActualReaderFixture.Create();giveUpFixture.SetKarminaPending();
  giveUpFixture.KarminaOnly.Read("B_End");giveUpFixture.SetKarminaChoice(2,false);
  ReadResult giveUp=giveUpFixture.KarminaOnly.Read("B_End");
  Check(giveUp.Valid&&giveUp.KarminaChoice==2
   &&giveUp.EndingSignals.Contains("ending.karmina_final_choice"),
   "actual Karmina reader accepts final Give Up choice after zero arm");
  Check(!pending.GolfBatteries.HasValue&&!pending.SurrenderChoice.HasValue,
   "Karmina reader needs no rating, stars, or Ending8 choice metadata");

  // Every Against alias maps to one physical event, and either final button
  // is accepted. The emitted adapter, rather than a direct core call, owns
  // the one-shot split assertion.
  foreach(string alias in aliases) foreach(int choice in new[]{1,2}){
   var fixture=ActualReaderFixture.Create();var action=NewActualKarmina(fixture.KarminaOnly,alias);
   action.ManualStart();fixture.SetKarminaPending();action.Tick("B_End");
   fixture.SetKarminaChoice(choice,false);action.Tick("B_End");
   Check(action.Splits==1&&HasLog(action,"karminaChoice="+choice),
    alias+" emits one split for final choice "+choice);
   action.Tick("B_End");Check(action.Splits==1,alias+" final choice is one-shot");
  }
  var all=ActualReaderFixture.Create();var allAction=NewActualKarmina(all.KarminaOnly,aliases[0]);
  allAction.settings[aliases[1]]=true;allAction.settings[aliases[2]]=true;allAction.ManualStart();
  all.SetKarminaPending();allAction.Tick("B_End");all.SetKarminaChoice(2,false);allAction.Tick("B_End");
  Check(allAction.Splits==1,"all three Against aliases share one final-choice event");

  // A first observed terminal button is not retroactive. It must follow a
  // qualified zero in the same attempt; both buttons obey that rule.
  foreach(int choice in new[]{1,2}){
   var first=ActualReaderFixture.Create();var firstAction=NewActualKarmina(first.KarminaOnly,aliases[0]);
   firstAction.ManualStart();first.SetKarminaChoice(choice,false);firstAction.Tick("B_End");
   Check(firstAction.Splits==0,"first observed Karmina terminal choice does not retrofire");
   first.SetKarminaPending();firstAction.Tick("B_End");first.SetKarminaChoice(choice,false);firstAction.Tick("B_End");
   Check(firstAction.Splits==1,"qualified Karmina zero can arm after an early terminal choice");
  }

  // Only the final source-qualified prompt can arm. Earlier Continue/Give Up
  // buttons, mismatched sides, and Texting mode are inert.
  foreach(int choice in new[]{1,2}){
   var wrongLine=ActualReaderFixture.Create();var wrongLineAction=NewActualKarmina(wrongLine.KarminaOnly,aliases[0]);
   wrongLineAction.ManualStart();wrongLine.SetKarmina(true,false,0,0,"B_E/m160","B_E/m162","B_E/m163");wrongLineAction.Tick("B_End");
   wrongLine.SetKarminaChoice(choice,false);wrongLineAction.Tick("B_End");
   Check(wrongLineAction.Splits==0,"earlier Karmina prompt line cannot arm choice "+choice);
  }
  var wrongLeft=ActualReaderFixture.Create();var wrongLeftAction=NewActualKarmina(wrongLeft.KarminaOnly,aliases[0]);
  wrongLeftAction.ManualStart();wrongLeft.SetKarmina(true,false,0,0,"B_E/m161","B_E/m160","B_E/m163");wrongLeftAction.Tick("B_End");
  wrongLeft.SetKarminaChoice(1,false);wrongLeftAction.Tick("B_End");
  Check(wrongLeftAction.Splits==0,"wrong Karmina left prompt cannot arm");
  var wrongRight=ActualReaderFixture.Create();var wrongRightAction=NewActualKarmina(wrongRight.KarminaOnly,aliases[0]);
  wrongRightAction.ManualStart();wrongRight.SetKarmina(true,false,0,0,"B_E/m161","B_E/m162","B_E/m160");wrongRightAction.Tick("B_End");
  wrongRight.SetKarminaChoice(2,false);wrongRightAction.Tick("B_End");
  Check(wrongRightAction.Splits==0,"wrong Karmina right prompt cannot arm");
  var texting=ActualReaderFixture.Create();var textingAction=NewActualKarmina(texting.KarminaOnly,aliases[0]);
  textingAction.ManualStart();texting.SetKarmina(true,true,0,0,"B_E/m161","B_E/m162","B_E/m163");textingAction.Tick("B_End");
  texting.SetKarminaChoice(1,false);textingAction.Tick("B_End");
  Check(textingAction.Splits==0,"Texting-mode Karmina prompt cannot arm");

  var menu=ActualReaderFixture.Create();var menuAction=NewActualKarmina(menu.KarminaOnly,aliases[0]);
  menuAction.ManualStart();menu.SetKarminaPending();menuAction.Tick("[Main Menu]");
  menu.SetKarminaChoice(1,false);menuAction.Tick("B_End");
  Check(menuAction.Splits==0,"menu-opening Karmina zero is not an arm");

  // Dialogue/chooser identity, line-array identity, and the outer scene
  // address are all boundaries for the retained zero arm.
  var identity=ActualReaderFixture.Create();var identityAction=NewActualKarmina(identity.KarminaOnly,aliases[0]);
  identityAction.ManualStart();identity.SetKarminaPending();identityAction.Tick("B_End");
  identity.SetKarminaIdentity(true,1);identityAction.Tick("B_End");
  Check(identityAction.Splits==0,"Karmina dialogue/chooser identity change rejects stale choice");
  identity.SetKarminaIdentity(false,0);identityAction.Tick("B_End");identity.SetKarminaChoice(1,false);identityAction.Tick("B_End");
  Check(identityAction.Splits==1,"canonical Karmina identity can rearm after rejection");

  var array=ActualReaderFixture.Create();var arrayAction=NewActualKarmina(array.KarminaOnly,aliases[0]);
  arrayAction.ManualStart();array.SetKarminaPending();arrayAction.Tick("B_End");array.SetKarminaArrayIdentity(1);arrayAction.Tick("B_End");
  Check(arrayAction.Splits==0,"Karmina line-array identity change rejects stale choice");
  array.SetKarminaPending();arrayAction.Tick("B_End");array.SetKarminaChoice(1,false);arrayAction.Tick("B_End");
  Check(arrayAction.Splits==1,"canonical Karmina line array can rearm");

  var address=ActualReaderFixture.Create();var addressAction=NewActualKarmina(address.KarminaOnly,aliases[0]);
  var addressReader=(ActualReaderFacade)addressAction.vars.Reader;addressAction.ManualStart();
  ((FakeHelper)addressAction.vars.Helper).Scenes.Active.Address=new IntPtr(0x1111);
  address.SetKarminaPending();addressAction.Tick("B_End");
  int addressResets=addressReader.KarminaBindingResetCalls;
  ((FakeHelper)addressAction.vars.Helper).Scenes.Active.Address=new IntPtr(0x2222);
  address.SetKarminaChoice(1,false);addressAction.Tick("B_End");
  Check(addressAction.Splits==0&&addressReader.KarminaBindingResetCalls>addressResets,
   "same-named B_End address change rejects stale Karmina choice");
  address.SetKarminaPending();addressAction.Tick("B_End");address.SetKarminaChoice(1,false);addressAction.Tick("B_End");
  Check(addressAction.Splits==1,"new B_End scene address can rearm Karmina choice");

  // Invalid reads preserve a qualified same-attempt zero, while explicit
  // reset and manual start clear it and do not retrofire a terminal sample.
  var gap=ActualReaderFixture.Create();var gapAction=NewActualKarmina(gap.KarminaOnly,aliases[0]);
  gapAction.ManualStart();gap.SetKarminaPending();gapAction.Tick("B_End");gap.SetInvalid();gapAction.Tick("B_End");
  gap.SetKarminaChoice(1,false);gapAction.Tick("B_End");
  Check(gapAction.Splits==1,"same-attempt invalid Karmina gap retains qualified zero");

  var reset=ActualReaderFixture.Create();var resetAction=NewActualKarmina(reset.KarminaOnly,aliases[0]);
  var resetReader=(ActualReaderFacade)resetAction.vars.Reader;resetAction.ManualStart();reset.SetKarminaPending();resetAction.Tick("B_End");
  resetAction.vars.TimerModel.Reset();resetAction.ManualStart();reset.SetKarminaChoice(1,false);resetAction.Tick("B_End");
  Check(resetAction.Splits==0&&resetReader.KarminaBindingResetCalls>=2,
   "onReset clears Karmina arm without retrofiring final choice");
  reset.SetKarminaPending();resetAction.Tick("B_End");reset.SetKarminaChoice(1,false);resetAction.Tick("B_End");
  Check(resetAction.Splits==1,"post-reset Karmina zero can rearm");

  var manual=ActualReaderFixture.Create();var manualAction=NewActualKarmina(manual.KarminaOnly,aliases[0]);
  var manualReader=(ActualReaderFacade)manualAction.vars.Reader;manualAction.ManualStart();manual.SetKarminaPending();manualAction.Tick("B_End");
  manual.SetKarminaChoice(1,false);manualAction.ManualStart();manualAction.Tick("B_End");
  Check(manualAction.Splits==0&&manualReader.KarminaBindingResetCalls>=2,
   "manual start clears Karmina arm without retrofiring final choice");
  manual.SetKarminaPending();manualAction.Tick("B_End");manual.SetKarminaChoice(1,false);manualAction.Tick("B_End");
  Check(manualAction.Splits==1,"post-manual-start Karmina zero can rearm");

  var paused=ActualReaderFixture.Create();var pausedAction=NewActualKarmina(paused.KarminaOnly,aliases[0]);
  pausedAction.ManualStart();paused.SetKarminaPending();pausedAction.Tick("B_End");pausedAction.timer.CurrentPhase=TimerPhase.Paused;
  paused.SetKarminaChoice(1,false);pausedAction.Tick("B_End");pausedAction.timer.CurrentPhase=TimerPhase.Running;pausedAction.Tick("B_End");
  Check(pausedAction.Splits==0,"paused Karmina terminal choice is consumed without a late finish");

  // Disabled metadata must not be polled. Enabling an alias later must use
  // the Karmina-specific retry latch, not the shared generic boundary latch.
  var disabled=ActualReaderFixture.Create();var disabledAction=NewActualKarmina(disabled.Initial,aliases[0]);
  disabledAction.settings[aliases[0]]=false;var disabledCode=new FakeCode();int disabledAttempts=0;
  disabledCode.CandidateFactory=delegate(){disabledAttempts++;return new ActualReaderFacade(disabled.KarminaOnly);};
  disabledAction.vars.Code=disabledCode;disabledAction.vars.MonoMetadata=new FakeMonoMetadata();disabledAction.ManualStart();
  disabled.SetKarminaPending();disabledAction.Tick("B_End");
  Check(disabledAttempts==0&&((FakeMonoMetadata)disabledAction.vars.MonoMetadata).Images.ClearCalls==0
   &&disabledAction.Splits==0,"disabled Against alias does not admit Karmina metadata");

  var recovery=ActualReaderFixture.Create();var recoveryAction=NewActualKarmina(recovery.Initial,aliases[0]);
  var recoveryOld=(ActualReaderFacade)recoveryAction.vars.Reader;var recoveryMetadata=new FakeMonoMetadata();int attempts=0;
  var recoveryCode=new FakeCode();recoveryCode.CandidateFactory=delegate(){attempts++;return attempts==1
   ? (object)new ActualReaderFacade(recovery.Initial) : new ActualReaderFacade(recovery.KarminaOnly);};
  recoveryAction.vars.Code=recoveryCode;recoveryAction.vars.ReaderConfigured=true;recoveryAction.vars.MonoMetadata=recoveryMetadata;
  recoveryAction.ManualStart();recovery.SetKarminaPending();recoveryAction.Tick("B_End");
  Check(attempts==1&&recoveryMetadata.Images.ClearCalls==(expectImageRefresh?1:0),
   "Karmina metadata candidate is attempted through emitted ASL");
  Check(ReferenceEquals((object)recoveryAction.vars.Reader,(object)recoveryOld)
   &&(string)recoveryAction.vars.LastKarminaMetadataScene=="",
   "failed isolated Karmina candidate is not admitted");
  recoveryAction.vars.OptionalMetadataRetryAfter=0L;recoveryAction.Tick("B_End");
  Check(attempts==2&&recoveryMetadata.Images.ClearCalls==(expectImageRefresh?2:0)
   &&ReferenceEquals((object)recoveryAction.vars.Reader,recoveryCode.LastCandidate)
   &&(string)recoveryAction.vars.LastKarminaMetadataScene=="B_End",
   "qualified isolated Karmina metadata is adopted independently");
  ReadResult recovered=((ActualReaderFacade)recoveryAction.vars.Reader).Read("B_End");
  Check(recovered.KarminaMetadataReady&&recovered.KarminaChoice==0,
   "adopted isolated reader emits pending Karmina zero");
  recovery.SetKarminaChoice(1,false);recoveryAction.Tick("B_End");
  Check(recoveryAction.Splits==1&&HasLog(recoveryAction,"karminaReady=True")
   &&HasLog(recoveryAction,"karminaChoice=1"),
   "isolated actual reader's final Karmina choice reaches emitted timer action");

  var late=ActualReaderFixture.Create();var lateAction=NewActualKarmina(late.Initial,aliases[0]);
  lateAction.settings[aliases[0]]=false;int lateAttempts=0;var lateCode=new FakeCode();
  lateCode.CandidateFactory=delegate(){lateAttempts++;return new ActualReaderFacade(late.KarminaOnly);};
  lateAction.vars.Code=lateCode;lateAction.vars.MonoMetadata=new FakeMonoMetadata();lateAction.ManualStart();
  late.SetKarminaPending();lateAction.Tick("B_End");Check(lateAttempts==0,"disabled late-enable alias does not pre-admit metadata");
  lateAction.settings[aliases[0]]=true;lateAction.vars.OptionalMetadataRetryAfter=0L;lateAction.Tick("B_End");
  Check(lateAttempts==1&&ReferenceEquals((object)lateAction.vars.Reader,lateCode.LastCandidate)
   &&(string)lateAction.vars.LastKarminaMetadataScene=="B_End",
   "late-enabled Against alias retries and records Karmina metadata independently");
  Console.WriteLine("KARMINA_CASES actual_reader=1 aliases=3 choices=2 all_aliases_once=1 first_terminal_guard=2 context_guards=7 identity_boundaries=3 reset_start=4 invalid_gap=1 paused=1 isolated_retry=1 late_enable=1");
 }
 static Actions NewActualTheodore(ActualReaderFixture fixture){
  var a=New();a.settings["autoStart"]=false;a.settings["splitWorlds"]=false;
  a.settings["splitScenes"]=false;a.settings["ending.9"]=true;
  a.vars.Reader=new ActualReaderFacade(fixture.Full);a.vars.ReaderConfigured=true;
  a.vars.SupportedBuild=true;a.vars.MonoMetadata=null;
  return a;
 }
 static void ActualTheodoreBindingLifecycleRegression(bool expectRefresh){
  // A same-scene invalid sample is only a transient read gap. The actual
  // reader must retain its qualified HP identity and emit Ending9 at zero HP.
  var gapFixture=ActualReaderFixture.Create();var gap=NewActualTheodore(gapFixture);
  var gapReader=(ActualReaderFacade)gap.vars.Reader;gap.ManualStart();gapFixture.SetBattle();
  gap.Tick("T_Boss");Check(gapReader.TheodoreBindingResetCalls==1&&gapReader.SurrenderBindingResetCalls==1&&gapReader.MerdekaBindingResetCalls==1&&gapReader.KarminaBindingResetCalls==1,
   "same-attempt case starts with all explicit binding resets");
  gapFixture.SetInvalid();gap.Tick("T_Boss");
  gapFixture.SetWin();gap.Tick("T_Boss");
  Check(gap.Splits==1,"same-scene invalid gap preserves actual HP binding for Ending9");
  gap.Tick("T_Boss");Check(gap.Splits==1,"same-scene invalid-gap Ending9 remains one-shot");

  // onReset clears only the reader HP binding. A run started while the
  // already-visible zero/cutscene sample is present must not retrofire.
  var resetFixture=ActualReaderFixture.Create();var reset=NewActualTheodore(resetFixture);
  var resetReader=(ActualReaderFacade)reset.vars.Reader;reset.ManualStart();resetFixture.SetBattle();
  reset.Tick("T_Boss");resetFixture.SetWin();reset.vars.TimerModel.Reset();
  Check(resetReader.TheodoreBindingResetCalls==2&&resetReader.SurrenderBindingResetCalls==2&&resetReader.MerdekaBindingResetCalls==2&&resetReader.KarminaBindingResetCalls==2,
   "onReset explicitly clears all old actual bindings");
  reset.ManualStart();reset.Tick("T_Boss");
  Check(reset.Splits==0,"onReset plus manual start at zero HP does not retrofire Ending9");
  resetFixture.SetBattle();reset.Tick("T_Boss");resetFixture.SetWin();reset.Tick("T_Boss");
  Check(reset.Splits==1,"new positive HP after explicit reset can rearm Ending9");

  // onStart is also a binding boundary even without TimerModel.Reset().
  var startFixture=ActualReaderFixture.Create();var start=NewActualTheodore(startFixture);
  var startReader=(ActualReaderFacade)start.vars.Reader;start.ManualStart();startFixture.SetBattle();
  start.Tick("T_Boss");startFixture.SetWin();start.ManualStart();start.Tick("T_Boss");
  Check(start.Splits==0&&startReader.TheodoreBindingResetCalls==2
   &&startReader.SurrenderBindingResetCalls==2&&startReader.MerdekaBindingResetCalls==2
   &&startReader.KarminaBindingResetCalls==2,
   "onStart at an already-terminal sample clears all bindings without retrofire");

  // A new T_Boss object can keep the same scene name. Its active address is
  // the instance boundary, so an old positive binding cannot accept HP zero.
  var identityFixture=ActualReaderFixture.Create();var identity=NewActualTheodore(identityFixture);
  var identityReader=(ActualReaderFacade)identity.vars.Reader;identity.ManualStart();
  ((FakeHelper)identity.vars.Helper).Scenes.Active.Address=new IntPtr(0x1111);
  identityFixture.SetBattle();identity.Tick("T_Boss");identityFixture.SetWin();
  ((FakeHelper)identity.vars.Helper).Scenes.Active.Address=new IntPtr(0x2222);
  identity.Tick("T_Boss");
  Check(identity.Splits==0&&identityReader.TheodoreBindingResetCalls>=3,
   "same-named T_Boss address change rejects old zero-HP terminal sample");
  identityFixture.SetBattle();identity.Tick("T_Boss");identityFixture.SetWin();identity.Tick("T_Boss");
  Check(identity.Splits==1,"same-named new scene instance can arm a fresh positive HP binding");

  // Active-scene invalidation is a hard boundary and clears the address token.
  var invalidFixture=ActualReaderFixture.Create();var invalid=NewActualTheodore(invalidFixture);
  var invalidReader=(ActualReaderFacade)invalid.vars.Reader;invalid.ManualStart();
  ((FakeHelper)invalid.vars.Helper).Scenes.Active.Address=new IntPtr(0x3333);
  invalidFixture.SetBattle();invalid.Tick("T_Boss");invalidFixture.SetWin();
  ((FakeHelper)invalid.vars.Helper).Scenes.Active.IsValid=false;invalid.Tick("T_Boss");
  Check(invalid.Splits==0&&(IntPtr)invalid.vars.TheodoreSceneAddress==IntPtr.Zero
   &&invalidReader.TheodoreBindingResetCalls>=3,"invalid active scene clears HP binding and address token");
  ((FakeHelper)invalid.vars.Helper).Scenes.Active.IsValid=true;invalidFixture.SetBattle();invalid.Tick("T_Boss");
  invalidFixture.SetWin();invalid.Tick("T_Boss");
  Check(invalid.Splits==1,"post-invalid active scene positive HP can rearm Ending9");

  // The helper can also report an identity change while Read() is in flight.
  // The emitted adapter must clear the binding and zero its scene token then.
  var midFixture=ActualReaderFixture.Create();var mid=NewActualTheodore(midFixture);
  var midReader=(ActualReaderFacade)mid.vars.Reader;mid.ManualStart();
  ((FakeHelper)mid.vars.Helper).Scenes.Active.Address=new IntPtr(0x5555);
  midFixture.SetBattle();midReader.AfterRead=delegate(string scene){
   ((FakeHelper)mid.vars.Helper).Scenes.Active.Address=new IntPtr(0x6666);
  };
  mid.Tick("T_Boss");
  Check(mid.Splits==0&&(IntPtr)mid.vars.TheodoreSceneAddress==IntPtr.Zero
   &&midReader.TheodoreBindingResetCalls>=3,"mid-read scene identity change clears HP binding and address token");
  midReader.AfterRead=null;((FakeHelper)mid.vars.Helper).Scenes.Active.Address=new IntPtr(0x7777);
  midFixture.SetBattle();mid.Tick("T_Boss");midFixture.SetWin();mid.Tick("T_Boss");
  Check(mid.Splits==1,"post-mid-read identity change positive HP can rearm Ending9");

  // Candidate adoption after a reset gets a new reader instance; it must not
  // inherit the old reader's positive binding at an already-terminal sample.
  if(expectRefresh){
   var candidateFixture=ActualReaderFixture.Create();var candidate=NewActualTheodore(candidateFixture);
   var oldReader=(ActualReaderFacade)candidate.vars.Reader;candidate.ManualStart();candidateFixture.SetBattle();
   candidate.Tick("T_Boss");candidate.vars.TimerModel.Reset();candidateFixture.SetWin();
   candidate.vars.MonoMetadata=new FakeMonoMetadata();candidate.vars.OptionalMetadataRetryAfter=0L;
   var candidateCode=new FakeCode();candidateCode.CandidateFactory=delegate(){return new ActualReaderFacade(candidateFixture);};
   candidate.vars.Code=candidateCode;
   ((FakeHelper)candidate.vars.Helper).Scenes.Active.Address=new IntPtr(0x4444);
   candidate.Tick("T_Boss");
   Check(candidate.Splits==0&&ReferenceEquals((object)candidate.vars.Reader,candidateCode.LastCandidate)
    &&oldReader.TheodoreBindingResetCalls>=2,
    "metadata candidate adoption after reset does not reuse prior HP binding");
  }
  Console.WriteLine("THEODORE_BINDING_CASES same_attempt_invalid_gap=1 explicit_reset=2 scene_instance_boundary=4 candidate_after_reset="+(expectRefresh?1:0));
 }
 static void TheodoreMetadataRetryRegression(){
  var a=New();a.settings["autoStart"]=false;a.settings["splitWorlds"]=false;a.settings["splitScenes"]=false;
  a.settings["ending.9"]=true;a.settings["ending.10"]=true;
  var first=new FakeReader();first.Sample.BoundaryMetadataReady=true;first.Sample.TheodoreMetadataReady=false;
  var second=new FakeReader();second.Sample.BoundaryMetadataReady=true;second.Sample.TheodoreMetadataReady=true;
  var code=new FakeCode();int attempts=0;code.CandidateFactory=delegate(){attempts++;return attempts==1?(object)first:second;};
  a.vars.Code=code;a.vars.Reader=new FakeReader();a.vars.ReaderConfigured=true;
  var metadata=new FakeMonoMetadata();a.vars.MonoMetadata=metadata;a.ManualStart();
  a.Tick("T_Boss");Check(attempts==1&&metadata.Images.ClearCalls==(__EXPECTED_REFRESH__?1:0),
   "Ending9 metadata candidate is attempted once");
  Check(!ReferenceEquals((object)a.vars.Reader,(object)first),
   "failed Ending9 metadata candidate is not adopted");
  int throttled=metadata.Images.ClearCalls;a.Tick("T_Boss");
  Check(metadata.Images.ClearCalls==throttled,
   "failed Ending9 metadata candidate is throttled");
  a.vars.OptionalMetadataRetryAfter=0L;a.Tick("T_Boss");
  Check(attempts==2&&metadata.Images.ClearCalls==(__EXPECTED_REFRESH__?2:0)
   &&ReferenceEquals((object)a.vars.Reader,(object)second),
   "qualified Ending9 metadata candidate is adopted after retry");
  Check((string)a.vars.LastTheodoreMetadataScene=="T_Boss",
   "qualified Ending9 metadata records its own scene latch");

  var ending10=new Actions();ending10.Setup();ending10.settings["enableTimers"]=true;
  ending10.settings["autoStart"]=false;ending10.settings["splitWorlds"]=false;ending10.settings["splitScenes"]=false;
  ending10.settings["ending.10"]=true;ending10.settings["ending.9"]=false;
  var tenReader=new FakeReader();var tenCode=new FakeCode{Candidate=tenReader};int tenAttempts=0;
  tenCode.CandidateFactory=delegate(){tenAttempts++;return tenReader;};
  ending10.vars.Code=tenCode;ending10.vars.Reader=new FakeReader();ending10.vars.ReaderConfigured=true;
  var tenMetadata=new FakeMonoMetadata();ending10.vars.MonoMetadata=tenMetadata;ending10.ManualStart();
  ending10.Tick("T_Boss");int tenClear=tenMetadata.Images.ClearCalls;ending10.Tick("T_Boss");
  Check(tenMetadata.Images.ClearCalls==tenClear&&tenClear==(__EXPECTED_REFRESH__?1:0),
   "Ending10-only T_Boss metadata path has no extra UI retry");
  ending10.settings["ending.9"]=true;ending10.vars.OptionalMetadataRetryAfter=0L;ending10.Tick("T_Boss");
  Check(tenAttempts==2&&tenMetadata.Images.ClearCalls==tenClear+(__EXPECTED_REFRESH__?1:0),
   "enabling Ending9 mid-scene forces its independent metadata retry");
 }
 public static int Main(string[] args) {
  try {
   LoggingPrivacyRegression();
   var a=New();a.settings["enableTimers"]=false;a.Tick("[Main Menu]");a.Tick("Office_1");Check(a.Starts==0,"disabled timer no start");
   a.settings["enableTimers"]=true;((FakeReader)a.vars.Reader).Sample.StartReady=true;a.Tick("Office_1");Check(a.Starts==1,"disabled start consumed only at actual ready");
   var b=New();FreshStart(b);int before=b.Splits;b.Tick("A_1");Check(b.Splits==before+1,"world split after start");
   var reader=(FakeReader)b.vars.Reader;reader.Sample.EndingSignals.Add("ending.execution_black");b.settings["ending.1"]=true;b.Tick("Boundary");Check(b.Splits==before+2,"physical ending action");b.Tick("Boundary");Check(b.Splits==before+2,"ending latch");
   LegacyTvBoundaryRegression(__EXPECTED_REFRESH__);
   var disabled=New();FreshStart(disabled);var disabledReader=(FakeReader)disabled.vars.Reader;disabledReader.Sample.EndingSignals.Add("ending.gem_dialogue");disabled.Tick("Subspace_Final");disabled.settings["ending.11"]=true;disabled.Tick("Subspace_Final");Check(disabled.Splits==0,"disabled ending does not replay");
   var invalid=New();FreshStart(invalid);var invalidReader=(FakeReader)invalid.vars.Reader;invalidReader.Sample.EndingSignals.Add("ending.theodore_victory");invalidReader.Sample.Valid=false;invalid.Tick("T_Boss");invalidReader.Sample.Valid=true;invalid.Tick("T_Boss");invalid.settings["ending.9"]=true;invalid.Tick("T_Boss");Check(invalid.Splits==0,"ending observed while disabled is consumed");
   var victory=New();FreshStart(victory);victory.settings["ending.9"]=true;((FakeReader)victory.vars.Reader).Sample.EndingSignals.Add("ending.theodore_victory");victory.Tick("T_Boss");Check(victory.Splits==1,"T_Boss reader signal produces Ending9");
   victory.Tick("T_Boss");Check(victory.Splits==1,"T_Boss Ending9 signal is one-shot");
   var phases=New();FreshStart(phases);var phaseReader=(FakeReader)phases.vars.Reader;phases.settings["ending.10"]=true;phaseReader.Sample.GolfBattleActive=true;phaseReader.Sample.GolfBatteries=1;phaseReader.Sample.GolfCutscenePlaying=false;phases.Tick("T_Boss");phaseReader.Sample.GolfBattleActive=false;phaseReader.Sample.GolfBatteries=0;phaseReader.Sample.GolfCutscenePlaying=true;phaseReader.Sample.EndingSignals.Add("ending.theodore_death");phases.timer.CurrentPhase=TimerPhase.Paused;phases.Tick("T_Boss");phases.timer.CurrentPhase=TimerPhase.Running;phases.Tick("T_Boss");Check(phases.Splits==0,"paused armed golf event consumed without action");
   ActualImageCacheLifecycleRegression(__EXPECTED_REFRESH__);
   ActualLateMetadataRegression(__EXPECTED_REFRESH__);
   ActualSurrenderChoiceRegression(__EXPECTED_REFRESH__);
   ActualKarminaFinalChoiceRegression(__EXPECTED_REFRESH__);
   ActualMerdekaOutcomeRegression();
   OptionalFriendLossRegression();
   ActualTvStartRegression(__EXPECTED_REFRESH__);
   ActualTheodoreBindingLifecycleRegression(__EXPECTED_REFRESH__);
   TheodoreMetadataRetryRegression();
   for(int mask=0;mask<8;mask++)foreach(var phase in new[]{TimerPhase.Running,TimerPhase.Paused}){
    var detach=New();FreshStart(detach);detach.timer.CurrentPhase=phase;
    detach.settings["enableTimers"]=(mask&1)!=0;detach.settings["autoReset"]=(mask&2)!=0;detach.settings.ResetEnabled=(mask&4)!=0;
    detach.Action_exit();
    Check(detach.Resets==(mask==7?1:0),"detach respects diagnostic/reset controls mask="+mask);
    if(mask!=7){
     Check(detach.timer.CurrentPhase==phase,"incomplete timer preserved for explicit rebind");
     detach.vars.SupportedBuild=true;detach.vars.ReaderConfigured=true;detach.vars.Helper=new FakeHelper();
     detach.settings["enableTimers"]=true;detach.timer.CurrentPhase=TimerPhase.Running;
     int prior=detach.Splits;detach.Tick("A_1");Check(detach.Splits==prior,"first reattached sample seeds without split");
     detach.Tick("A_7");Check(detach.Splits==prior+1,"reattach does not leave orphan timer mask="+mask);
    }
   }
   var endedDetach=New();FreshStart(endedDetach);var endedDetachReader=(FakeReader)endedDetach.vars.Reader;endedDetach.timer.CurrentPhase=TimerPhase.Ended;endedDetach.Action_exit();Check(endedDetach.Resets==0&&endedDetach.timer.CurrentPhase==TimerPhase.Ended&&endedDetachReader.TheodoreBindingResetCalls==2&&endedDetachReader.SurrenderBindingResetCalls==2&&endedDetachReader.MerdekaBindingResetCalls==2&&endedDetachReader.KarminaBindingResetCalls==2,"detach preserves Ended result and resets all reader bindings");
   var shutdown=New();var shutdownReader=(FakeReader)shutdown.vars.Reader;shutdown.Action_shutdown();Check(shutdownReader.TheodoreBindingResetCalls==1&&shutdownReader.SurrenderBindingResetCalls==1&&shutdownReader.MerdekaBindingResetCalls==1&&shutdownReader.KarminaBindingResetCalls==1,"shutdown resets all reader bindings through narrow APIs");
   var noLrt=New();Check(!noLrt.Action_isLoading(),"load-removal remains false");
   if(args.Length>0){
    var identity=New();identity.modules.Add(new ModuleStub{FileName=Path.Combine(args[0],"UnityPlayer.dll")});
    identity.vars.MonoMetadata=new FakeMonoMetadata();identity.vars.LastMetadataScene="T_Boss";identity.vars.LastTvStartMetadataScene="B_Aftermath";
    identity.Action_init();Check((bool)identity.vars.SupportedBuild,"real original file hashes accepted");
    Check(identity.vars.MonoMetadata==null&&(string)identity.vars.LastMetadataScene==""
     &&(string)identity.vars.LastTvStartMetadataScene=="","normal attach discards prior Mono/TV metadata identity");
    Check(!((FakeHelper)identity.vars.Helper).TryLoad(null)&&!(bool)identity.vars.ReaderConfigured,"missing Mono metadata fails closed after real build identity check");
    identity.vars.MetadataRetryAfter=0L;var checkpointUnknown=new FakeReader();checkpointUnknown.Sample.HasCheckpoint=null;
    identity.vars.Code=new FakeCode{Candidate=checkpointUnknown};
    Check(((FakeHelper)identity.vars.Helper).TryLoad(new FakeMonoMetadata())&&(bool)identity.vars.ReaderConfigured,"explicit valid reader fixture with unknown checkpoint does not block other observations");
    identity.vars.Code=typeof(StateMachine).Assembly;
    identity.vars.LastTrace="stale";identity.vars.LastFault="stale";identity.Action_exit();Check((string)identity.vars.LastTrace==""&&(string)identity.vars.LastFault==""
     &&(string)identity.vars.LastTvStartMetadataScene=="","exit clears diagnostics and TV metadata identity");
   } else Console.WriteLine("SKIP original-build hash fixture: set PEPPERED_GAME_ROOT");
   Console.WriteLine("PASS ASL adapter fixtures: "+count+" assertions; all action bodies compiled. NOT live Windows validation.");return 0;
  }catch(Exception e){Console.WriteLine(e);return 1;}
 }
}
'''

def run_variant(text, expected_refresh, label):
    methods = '\n'.join('public '+ret+' Action_'+name+'() {'+body(text,name)+'\n}' for name,ret in ACTIONS.items())
    settings = '\n'.join(line for line in body(text,'startup').splitlines() if line.strip().startswith('settings.Add('))
    harness = HARNESS.replace('__METHODS__',methods).replace('__SETTINGS__',settings).replace(
        '__LOGGER_SETUP__', emitted_logger_setup(text)).replace(
        '__EXPECTED_REFRESH__', 'true' if expected_refresh else 'false'
    )
    with tempfile.TemporaryDirectory(prefix='peppered-asl-adapter-') as temp:
        src=Path(temp)/'Adapter.cs';src.write_text(harness, encoding='utf-8')
        out=Path(temp)/'Adapter.exe'
        cmd=['mcs','-sdk:4.8','-langversion:4','-r:Microsoft.CSharp','-out:'+str(out),
             str(ROOT/'src/Logic.cs'),str(ROOT/'src/ReadOnlyReader.cs'),str(src)]
        result=subprocess.run(cmd,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        if result.returncode != 0:
            raise RuntimeError(label + ' compile failed:\n' + result.stdout)
        env=dict(os.environ,MONO_PATH=str(ROOT/'Components'))
        game_root=os.environ.get('PEPPERED_GAME_ROOT')
        args=['mono',str(out)] + ([game_root] if game_root else [])
        sandbox=Path(temp)/'private-components-cwd'
        sandbox.mkdir()
        result=subprocess.run(args,env=env,cwd=sandbox,text=True,stdout=subprocess.PIPE,
                              stderr=subprocess.STDOUT,timeout=30)
        print(label + ': ' + result.stdout.strip())
        if result.returncode != 0:
            raise RuntimeError(label + ' adapter fixture failed: ' + str(result.returncode))


def main():
    text = render_emitted_asl()
    validate_settings(text)
    legacy = text.replace(
        '            // Match the helper OnTryLoadFailure: refresh cached image wrappers.\n'
        '            vars.MonoMetadata.Images.Clear();\n',
        '',
    )
    if legacy == text:
        raise RuntimeError('unable to derive private pre-refresh control from emitted RC14 text')
    run_variant(text, True, 'RC14 emitted-ASL (legacy RC8/RC9/RC10 regressions)')
    run_variant(legacy, False, 'pre-refresh control')
    print(json.dumps({'compiled_actions':list(ACTIONS),
                      'image_cache_regression':{'rc8_emitted':True,'pre_refresh_control':True},
                      'live_windows_runtime':False}))

if __name__=='__main__':main()
