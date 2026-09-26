using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Peppered;
// Explicit offline Mono metadata/memory fixture; no target game process.
public class MetaType {
 public string ElementType;public IntPtr Data;private object classValue;public int ClassReads;public bool PoisonClass;
 public object Class { get { ClassReads++;if(PoisonClass)throw new Exception("poisoned GenericInst.Class");return classValue; } set { classValue=value; } }
}
public class MetaField { public string Name; public int Offset; public MetaType Type; public bool IsStatic; }
public class MetaClass : IEnumerable {
 static Dictionary<long,MetaClass> registry=new Dictionary<long,MetaClass>();
 public string Name;public string Namespace;public IntPtr Address;public IntPtr Static;public List<MetaField> Fields=new List<MetaField>();
 public MetaClass(){}
 public MetaClass(IntPtr address){MetaClass source;if(!registry.TryGetValue(address.ToInt64(),out source))throw new Exception("unknown trusted class");Name=source.Name;Namespace=source.Namespace;Address=source.Address;Static=source.Static;Fields=source.Fields;}
 public MetaClass Register(){if(Address==IntPtr.Zero)throw new Exception("unaddressed class");registry[Address.ToInt64()]=this;return this;}
 public IEnumerator GetEnumerator(){foreach(MetaField f in Fields)yield return f;}
 public MetaClass Add(string n,int off,MetaType t,bool isStatic=false){Fields.Add(new MetaField{Name=n,Offset=off,Type=t,IsStatic=isStatic});return this;}
}
public class MetaImage {
 public string Name;public IntPtr Address;public Dictionary<string,MetaClass> Classes=new Dictionary<string,MetaClass>();
 public MetaImage Add(string fullName,MetaClass klass){Classes[fullName]=klass;return this;}
 public MetaClass this[string fullName]{get{return Classes[fullName];}}
}
public class MetaMono {
 public MetaClass Scene,State,Dialogue,Player,ButtonChoose,Dictionary,Entry;public MetaImage TextMeshPro,UnityUI,CoreModule;
 public object this[string name]{get{if(name=="MySceneManager")return Scene;if(name=="WHAT_HAVE_I_DONE")return State;if(name=="DialogueManager")return Dialogue;if(name=="Button_Choose")return ButtonChoose;if(name=="Player")return Player;throw new Exception("unknown fixture class");}}
 public MetaImage GetImage(string name){if(name=="Unity.TextMeshPro")return TextMeshPro;if(name=="UnityEngine.UI")return UnityUI;if(name=="UnityEngine.CoreModule")return CoreModule;throw new Exception("unknown fixture image");}
 public MetaClass GetClass(MetaImage image,string name){return image[name];}
 public IntPtr ClassParent(IntPtr klass){
  if(klass==new IntPtr(11009))return new IntPtr(11001); // Button -> Selectable
  if(klass==new IntPtr(11010))return new IntPtr(11011); // owned game subtype -> base
  if(klass==new IntPtr(11011))return new IntPtr(11001);
  if(klass==new IntPtr(11012))return new IntPtr(11012); // invalid cyclic metadata
  return IntPtr.Zero;
 }
}
public class MemoryHelper {
 public Dictionary<long,long> Values=new Dictionary<long,long>();public Dictionary<long,byte[]> Bytes=new Dictionary<long,byte[]>();
 public List<long> ReadAddresses=new List<long>();public int UnboundedCalls,MaxByteRead,LengthReads;public bool ChangeLengthAfterCheck;
 public long GenericClassPointerAddress=-1,GenericClassValueAfterFirstRead=-1;public int GenericClassReads;
 public Action<MemoryHelper,long> AfterRead;
 public int CheckpointPointerReads;public long CheckpointValueAfterFirstRead=-1;public bool FailCheckpointAfterFirstRead;
 public bool TryRead<T>(out T value,IntPtr p) {
  value=default(T);long raw;ReadAddresses.Add(p.ToInt64());
  if(p.ToInt64()==4104){CheckpointPointerReads++;if(FailCheckpointAfterFirstRead&&CheckpointPointerReads>1)return false;if(CheckpointValueAfterFirstRead>=0&&CheckpointPointerReads>1){value=(T)Convert.ChangeType(CheckpointValueAfterFirstRead,typeof(T));return true;}}
  if(p.ToInt64()==GenericClassPointerAddress){GenericClassReads++;if(GenericClassValueAfterFirstRead>=0&&GenericClassReads>1){value=(T)Convert.ChangeType(GenericClassValueAfterFirstRead,typeof(T));return true;}}
  if(p.ToInt64()==20496){LengthReads++;if(ChangeLengthAfterCheck&&LengthReads>1){value=(T)Convert.ChangeType(1000000000,typeof(T));return true;}}
  if(!Values.TryGetValue(p.ToInt64(),out raw))return false;
  if(typeof(T)==typeof(bool))value=(T)(object)(raw!=0);
  else value=(T)Convert.ChangeType(raw,typeof(T));
  Action<MemoryHelper,long> callback=AfterRead;
  if(callback!=null){AfterRead=null;callback(this,p.ToInt64());}
  return true;
 }
 public bool TryReadSpan<T>(out T[] value,int length,IntPtr p){value=null;MaxByteRead=Math.Max(MaxByteRead,length);byte[] b;
  if(typeof(T)!=typeof(byte)||length>128||!Bytes.TryGetValue(p.ToInt64(),out b)||b.Length<length)return false;
  byte[] exact=new byte[length];Array.Copy(b,exact,length);value=(T[])(object)exact;
  Action<MemoryHelper,long> callback=AfterRead;
  if(callback!=null){AfterRead=null;callback(this,p.ToInt64());}
  return true;
 }
 public bool TryReadString(out string s,bool deref,IntPtr p){UnboundedCalls++;s="Larry";return true;}
}
public static class ReaderBridgeTests {
 static int assertions,failures;
 static void Check(bool v,string n){assertions++;if(!v){failures++;Console.WriteLine("FAIL "+n);}}
 static MetaType Primitive(string n){return new MetaType{ElementType=n};}
 static MetaType ArrayType(IntPtr elementClass){return new MetaType{ElementType="SzArray",Data=elementClass};}
 static MetaType RefType(MetaClass klass){return new MetaType{ElementType="Class",Class=klass};}
 static MetaMono Metadata(){
  var int32=new MetaClass{Name="Int32",Namespace="System",Address=new IntPtr(13001)}.Register();
  var text=new MetaClass{Name="String",Namespace="System",Address=new IntPtr(13002)}.Register();
  var entry=new MetaClass{Name="Entry",Namespace="",Address=new IntPtr(13003)}
   .Add("hashCode",0,Primitive("I4")).Add("next",4,Primitive("I4"))
   .Add("key",8,Primitive("String")).Add("value",16,Primitive("I4"))
   .Register();
  var dict=new MetaClass{Name="Dictionary_2",Namespace="System.Collections.Generic",Address=new IntPtr(13004)}
   .Add("_buckets",16,ArrayType(int32.Address)).Add("_entries",24,ArrayType(entry.Address))
   .Add("_comparer",32,Primitive("GenericInst")).Add("_keys",40,Primitive("GenericInst"))
   .Add("_values",48,Primitive("GenericInst")).Add("_syncRoot",56,Primitive("Object"))
   .Add("_count",64,Primitive("I4")).Add("_freeList",68,Primitive("I4"))
   .Add("_freeCount",72,Primitive("I4")).Add("_version",76,Primitive("I4"))
   .Register();
  // Real Player.CanMove is a computed property; its setter writes _canMove.
  // Mono field enumeration exposes only this private backing field.
  var player=new MetaClass{Name="Player"}.Add("_canMove",40,Primitive("Boolean"));
  var scene=new MetaClass{Name="MySceneManager",Static=new IntPtr(4096)};
  scene.Add("Abyss_State",0,Primitive("I4"),true).Add("CheckPointLvlName",8,Primitive("String"),true)
   .Add("CutscenePlaying",16,Primitive("Boolean"),true).Add("DeadState",20,Primitive("I4"),true)
   .Add("instance",24,new MetaType{ElementType="Class",Class=scene},true)
   .Add("_player",32,new MetaType{ElementType="Class",Class=player},false);
  var tmpText=new MetaClass{Name="TMP_Text",Namespace="TMPro",Address=new IntPtr(1001)}.Add("m_maxVisibleCharacters",1268,Primitive("I4"));
  var textBox=new MetaClass{Name="TextMeshProUGUI",Namespace="TMPro",Address=new IntPtr(1002)};
  var dialogue=new MetaClass{Name="DialogueManager",Address=new IntPtr(1601),Static=new IntPtr(24576)};
  var buttonChoose=new MetaClass{Name="Button_Choose",Namespace="",Address=new IntPtr(1602)}
   .Add("ButtonChoice",32,Primitive("I4"));
  dialogue.Add("Instance",0,new MetaType{ElementType="Class",Class=dialogue},true).Add("Playing",196,Primitive("Boolean"),false)
   .Add("DILines",96,new MetaType{ElementType="SzArray",Class=new MetaClass{Name="String[]"}},false)
   .Add("CurrentLineIndex",188,Primitive("I4"))
   .Add("TextBox",32,new MetaType{ElementType="Class",Class=textBox},false)
   .Add("LeftString",80,new MetaType{ElementType="String",Class=text},false)
   .Add("RightString",88,new MetaType{ElementType="String",Class=text},false)
   .Add("Texting",200,Primitive("Boolean"),false)
   .Add("BChoose",40,new MetaType{ElementType="Class",Class=buttonChoose},false);
  var choicesType=new MetaType{ElementType="GenericInst",Data=new IntPtr(14001),PoisonClass=true};
  var collectiblesType=new MetaType{ElementType="GenericInst",Data=new IntPtr(14002),PoisonClass=true};
  var state=new MetaClass{Name="WHAT_HAVE_I_DONE",Static=new IntPtr(8192)}
   .Add("Choices",0,choicesType,true).Add("Collectibles",8,collectiblesType,true);
  var mode=new MetaClass{Name="Mode",Namespace="",Address=new IntPtr(11007)};
  var direction=new MetaClass{Name="Direction",Namespace="",Address=new IntPtr(11008)};
  var navigation=new MetaClass{Name="Navigation",Namespace="UnityEngine.UI",Address=new IntPtr(11003)}
   .Add("m_Mode",0,new MetaType{ElementType="ValueType",Class=mode});
  var objectClass=new MetaClass{Name="Object",Namespace="UnityEngine",Address=new IntPtr(11004)}
   .Add("m_CachedPtr",16,Primitive("I"));
  var rectTransform=new MetaClass{Name="RectTransform",Namespace="UnityEngine",Address=new IntPtr(11005)};
  var graphic=new MetaClass{Name="Graphic",Namespace="UnityEngine.UI",Address=new IntPtr(11006)};
  var selectable=new MetaClass{Name="Selectable",Namespace="UnityEngine.UI",Address=new IntPtr(11001),Static=new IntPtr(50000)}
   .Add("s_Selectables",0,ArrayType(new IntPtr(11001)),true)
   .Add("s_SelectableCount",8,Primitive("I4"),true)
   .Add("m_Navigation",24,new MetaType{ElementType="ValueType",Class=navigation})
   .Add("m_TargetGraphic",104,RefType(graphic))
   .Add("m_CurrentIndex",220,Primitive("I4"));
  var slider=new MetaClass{Name="Slider",Namespace="UnityEngine.UI",Address=new IntPtr(11002)}
   .Add("m_FillRect",232,RefType(rectTransform))
   .Add("m_HandleRect",240,RefType(rectTransform))
   .Add("m_Direction",296,new MetaType{ElementType="ValueType",Class=direction})
   .Add("m_MinValue",300,Primitive("R4"))
   .Add("m_MaxValue",304,Primitive("R4"))
   .Add("m_WholeNumbers",308,Primitive("Boolean"))
   .Add("m_Value",312,Primitive("R4"));
  return new MetaMono{Scene=scene,Player=player,TextMeshPro=new MetaImage{Name="Unity.TextMeshPro"}.Add("TMPro.TMP_Text",tmpText).Add("TMPro.TextMeshProUGUI",textBox),
    UnityUI=new MetaImage{Name="UnityEngine.UI",Address=new IntPtr(15000)}.Add("UnityEngine.UI.Selectable",selectable).Add("UnityEngine.UI.Slider",slider).Add("UnityEngine.UI.Navigation",navigation).Add("UnityEngine.UI.Graphic",graphic),
    CoreModule=new MetaImage{Name="UnityEngine.CoreModule",Address=new IntPtr(15001)}.Add("UnityEngine.Object",objectClass).Add("UnityEngine.RectTransform",rectTransform).Add("UnityEngine.UI.Graphic",graphic),
    State=state,Dialogue=dialogue,ButtonChoose=buttonChoose,Dictionary=dict,Entry=entry};
  }
 static MemoryHelper Memory(){var m=new MemoryHelper();
  m.Values[4096]=49;m.Values[4104]=0;m.Values[4112]=0;m.Values[4116]=0;m.Values[4120]=45000;m.Values[8192]=12288;m.Values[8200]=0;
  m.Values[24576]=28672;m.Values[28672]=28680;m.Values[28680]=1601;m.Values[28712]=40000;m.Values[40000]=40008;m.Values[40008]=1602;m.Values[40032]=0;m.Values[28868]=1;m.Values[28768]=36000;m.Values[28860]=0;m.Values[28704]=35000;m.Values[28752]=37100;m.Values[28760]=37150;m.Values[28872]=0;m.Values[36268]=1;m.Values[36024]=1;m.Values[36032]=37000;m.Values[37016]=7;
  m.Values[37116]=7;m.Bytes[37120]=Encoding.Unicode.GetBytes("B_M/m41");m.Values[37166]=7;m.Bytes[37170]=Encoding.Unicode.GetBytes("B_M/m42");
  m.Values[45032]=46000;m.Values[46040]=1;
  m.Values[12312]=16384;m.Values[12352]=1;m.Values[12356]=0;m.Values[12360]=0;m.Values[12364]=1;m.Values[16408]=1;
  m.Values[14033]=13004;m.Values[14034]=13004;
  m.Values[16416]=12345;m.Values[16420]=-1;m.Values[16424]=20480;m.Values[16432]=1;m.Values[20496]=5;
  m.Values[50000]=60000;m.Values[50008]=1;m.Values[60024]=1;m.Values[60032]=70000;
  m.Values[70000]=73000;m.Values[73000]=11002;m.Values[70016]=80000;m.Values[70024]=0;
  m.Values[70104]=0;m.Values[70220]=0;m.Values[70232]=70200;m.Values[70240]=0;
  m.Values[70296]=0;m.Values[70300]=0;m.Values[70304]=1120403456;m.Values[70308]=1;m.Values[70312]=1120403456;
  m.Values[70200]=74000;m.Values[74000]=11005;m.Values[70216]=81000;
  m.Bytes[20500]=Encoding.Unicode.GetBytes("Larry");m.Bytes[37020]=Encoding.Unicode.GetBytes("Gem/m11");return m;
 }
 static void DialogueLine(MemoryHelper m,string line){m.Values[37016]=line.Length;m.Bytes[37020]=Encoding.Unicode.GetBytes(line);}
 static Snapshot StartSample(ReadOnlyReader reader,string scene){
  var r=reader.Read(scene);
  return new Snapshot{Valid=r.Valid,Scene=scene,Abyss=r.Abyss,HasCheckpoint=r.HasCheckpoint,
    StartReady=r.StartReady,TheodoreHp=r.TheodoreHp,
    TheodoreMetadataReady=r.TheodoreMetadataReady,
    BoundaryMetadataReady=r.BoundaryMetadataReady,EndingSignals=r.EndingSignals};
 }
 static void RealStartBridge(){
  var memory=Memory();memory.Values[4096]=0;memory.Values[4112]=1;memory.Values[46040]=0;
  var metadata=Metadata();metadata.State=null;metadata.Dialogue=null;
  var reader=new ReadOnlyReader();reader.Configure(memory,metadata);
  var core=new StateMachine();var options=new Options{Enabled=true,AutoStart=true,BasicStart=true};
  core.Step(StartSample(reader,"[Main Menu]"),"NotRunning",options);
  Check(!core.Step(StartSample(reader,"Office_1"),"NotRunning",options).Start,"real reader/core bridge does not start on intro entry");
  memory.Values[46040]=1;memory.Values[4112]=0;
  var ready=StartSample(reader,"Office_1");
  Check(ready.Valid&&ready.StartReady==true&&ready.HasCheckpoint==false,"real backing field resolves with optional metadata unavailable");
  Check(core.Step(ready,"NotRunning",options).Start,"real reader/control handoff reaches core auto-start");
  core.OnStart(true);
  Check(!core.Step(ready,"Running",options).Start,"accepted auto-start is one-shot");
  core.OnReset();core.Step(StartSample(reader,"[Main Menu]"),"NotRunning",options);
  Check(core.Step(ready,"NotRunning",options).Start,"real reader/core second run can auto-start");
  core.OnStart(true);core.OnReset();
  memory.Values[4104]=52000;memory.Values[52016]="Office_1".Length;memory.Bytes[52020]=Encoding.Unicode.GetBytes("Office_1");
  core.Step(StartSample(reader,"[Main Menu]"),"NotRunning",options);
  var resumed=StartSample(reader,"Office_1");
  Check(resumed.HasCheckpoint==true&&!core.Step(resumed,"NotRunning",options).Start,"real checkpoint still vetoes auto-start");
 }
 static void TheodoreSessionAndRegistryGuards(){
  foreach(int secondClass in new int[]{11005,11009,11010,11012,99000}){
   var m=Memory();m.Values[50008]=2;m.Values[60024]=2;m.Values[60040]=90000;m.Values[90000]=91000;m.Values[91000]=secondClass;
   var reader=new ReadOnlyReader();reader.Configure(m,Metadata());var sample=reader.Read("T_Boss");
   bool legal=secondClass==11009||secondClass==11010;
   Check(sample.Valid&&sample.TheodoreMetadataReady&&sample.TheodoreHp.HasValue==legal,
    "counted registry slot ancestry with valid Slider: "+secondClass);
  }
  var memory=Memory();var activeReader=new ReadOnlyReader();activeReader.Configure(memory,Metadata());
  Check(activeReader.Read("T_Boss").TheodoreHp==100,"explicit boundary fixture armed");
  memory.Values.Remove(4096);Check(!activeReader.Read("T_Boss").Valid,"same-attempt gap is invalid");
  memory.Values[4096]=300;memory.Values[70312]=0;memory.Values[4112]=1;
  Check(activeReader.Read("T_Boss").EndingSignals.Contains("ending.theodore_victory"),"same validated identity survives transient gap");
  activeReader.ResetTheodoreBinding();
  Check(!activeReader.Read("T_Boss").EndingSignals.Contains("ending.theodore_victory"),"explicit boundary cannot reuse old positive HP");
  memory.Values[70312]=1120403456;memory.Values[4112]=0;activeReader.Read("T_Boss");
  activeReader.ResetTheodoreBinding();memory.Values[70312]=0;memory.Values[4112]=1;
  Check(!activeReader.Read("T_Boss").EndingSignals.Contains("ending.theodore_victory"),"reset between positive HP and already-ended sample rejects retro finish");
  memory.Values[70312]=1120403456;memory.Values[4112]=0;activeReader.Read("T_Boss");
  memory.Values[70312]=0;memory.Values[4112]=1;
  Check(activeReader.Read("T_Boss").EndingSignals.Contains("ending.theodore_victory"),"new positive after boundary rearms independent attempt");
 }
 static void TheodoreMetadataGuards(){
  Action<MetaMono,string,string> floatKind=delegate(MetaMono mono,string field,string kind){
   mono.UnityUI.Classes["UnityEngine.UI.Slider"].Fields.Find(f=>f.Name==field).Type=Primitive(kind);
  };
  foreach(string kind in new string[]{"R4","12","11","U8","R8","I4"}){
   var meta=Metadata();floatKind(meta,"m_Value",kind);var reader=new ReadOnlyReader();reader.Configure(Memory(),meta);var value=reader.Read("T_Boss");
   Check(value.Valid&&value.BoundaryMetadataReady&&value.TheodoreMetadataReady==(kind=="R4"||kind=="12"),"Theodore scalar kind qualification "+kind);
  }
  var mutations=new Action<MetaMono>[] {
   x=>x.UnityUI.Address=IntPtr.Zero,
   x=>x.UnityUI.Classes["UnityEngine.UI.Slider"].Namespace="Wrong.Image",
   x=>x.UnityUI.Classes["UnityEngine.UI.Selectable"].Fields.Find(f=>f.Name=="s_Selectables").Type=Primitive("Class"),
   x=>x.UnityUI.Classes["UnityEngine.UI.Selectable"].Fields.Find(f=>f.Name=="s_SelectableCount").Type=Primitive("R4"),
   x=>x.UnityUI.Classes["UnityEngine.UI.Selectable"].Fields.Find(f=>f.Name=="s_SelectableCount").IsStatic=false,
   x=>x.CoreModule.Classes["UnityEngine.Object"].Fields.Find(f=>f.Name=="m_CachedPtr").Type=Primitive("I4"),
   x=>((MetaClass)x.UnityUI.Classes["UnityEngine.UI.Slider"].Fields.Find(f=>f.Name=="m_Direction").Type.Class).Namespace="Forged.Namespace",
   x=>((MetaClass)x.UnityUI.Classes["UnityEngine.UI.Slider"].Fields.Find(f=>f.Name=="m_Direction").Type.Class).Name="NotDirection",
   x=>x.UnityUI.Classes["UnityEngine.UI.Navigation"].Fields.Find(f=>f.Name=="m_Mode").Offset=16,
   x=>x.UnityUI.Classes["UnityEngine.UI.Slider"].Fields.Find(f=>f.Name=="m_Value").Offset=0,
   x=>x.UnityUI.Classes["UnityEngine.UI.Slider"].Fields.Find(f=>f.Name=="m_WholeNumbers").Type=Primitive("I4"),
   x=>x.CoreModule.Classes["UnityEngine.RectTransform"]=new MetaClass{Name="RectTransform",Namespace="UnityEngine",Address=new IntPtr(999999)}
  };
  int index=0;
  foreach(var change in mutations){
   var meta=Metadata();change(meta);var memory=Memory();memory.Values[20496]=9;memory.Bytes[20500]=Encoding.Unicode.GetBytes("Batteries");
   var reader=new ReadOnlyReader();reader.Configure(memory,meta);var value=reader.Read("T_Boss");
   Check(value.Valid&&value.BoundaryMetadataReady&&!value.TheodoreMetadataReady&&!value.TheodoreHp.HasValue
     &&value.GolfBattleActive==true,"Theodore optional metadata rejection preserves Ending10 path "+index++);
  }
 }
 static void TvStartBridge(){
  var m=Memory();DialogueLine(m,"B_A/m36");
  var reader=new ReadOnlyReader();reader.Configure(m,Metadata());
  var active=reader.Read("B_Aftermath");
  Check(active.Valid&&active.BoundaryMetadataReady&&active.TvStartMetadataReady
    &&active.TvStartActive==true
    &&active.EndingSignals.Contains("ending.karmina_tv_start"),
    "actual Configure resolves minimal B_Aftermath TV-start payload");
  Check(!m.ReadAddresses.Contains(28704)&&!m.ReadAddresses.Contains(28872),
    "actual TV-start path does not read TMP or chooser fields");
  m.Values[28868]=0;
  var inactive=reader.Read("B_Aftermath");
  Check(inactive.Valid&&inactive.TvStartActive==false
    &&inactive.EndingSignals.Count==0,
    "actual TV-start path accepts Playing false as inactive");
  m.Values[28868]=1;m.Values[37016]=6;m.Bytes[37020]=Encoding.Unicode.GetBytes("B_A/m1");
  var wrongLine=reader.Read("B_Aftermath");
  Check(wrongLine.Valid&&wrongLine.TvStartActive==false
    &&wrongLine.EndingSignals.Count==0,
    "actual TV-start path rejects wrong producer line");
  var noGlyph=Metadata();noGlyph.TextMeshPro=null;noGlyph.State=null;noGlyph.ButtonChoose=null;
  m=Memory();DialogueLine(m,"B_A/m36");reader=new ReadOnlyReader();reader.Configure(m,noGlyph);
  var shared=reader.Read("B_Aftermath");
  Check(shared.Valid&&shared.TvStartMetadataReady&&shared.BoundaryMetadataReady
    &&shared.TvStartActive==true
    &&shared.EndingSignals.Contains("ending.karmina_tv_start"),
    "TV-start survives full glyph metadata failure");
 }
 static void SurrenderBridge(){
  var memory=Memory();DialogueLine(memory,"B_M/m45");memory.Values[28868]=1;memory.Values[28872]=0;memory.Values[40032]=0;
  var reader=new ReadOnlyReader();reader.Configure(memory,Metadata());
  var pending=reader.Read("B_Merdeka");
  Check(pending.Valid&&pending.BoundaryMetadataReady&&pending.SurrenderMetadataReady
    &&pending.SurrenderChoice==0&&pending.SurrenderPhase=="pending"
    &&pending.EndingSignals.Count==0,"actual Configure resolves final surrender pending context");
  memory.Values[40032]=1;memory.Values[28868]=0;
  var confirmed=reader.Read("B_Merdeka");
  Check(confirmed.Valid&&confirmed.SurrenderChoice==1&&confirmed.SurrenderPhase=="confirmed"
    &&confirmed.EndingSignals.Contains("ending.merdeka_surrender"),"final choice one confirms Ending8");
  reader.ResetSurrenderBinding();memory.Values[40032]=1;
  Check(reader.Read("B_Merdeka").EndingSignals.Count==0,"initial stale choice one cannot arm Ending8");
  var bad=Metadata();bad.Dialogue.Fields.Find(f=>f.Name=="BChoose").Type=Primitive("I4");
  reader.Configure(memory,bad);var failed=reader.Read("B_Merdeka");
  Check(failed.Valid&&!failed.BoundaryMetadataReady&&!failed.SurrenderMetadataReady,"surrender metadata failure is optional");
 }
 public static int Main(){
  RealStartBridge();
  TheodoreMetadataGuards();
  TheodoreSessionAndRegistryGuards();
  TvStartBridge();
  SurrenderBridge();
  var reader=new ReadOnlyReader();var m=Memory();var qualified=Metadata();reader.Configure(m,qualified);var r=reader.Read("B_6.6");
  Check(r.Valid&&r.Abyss==49,"dynamic metadata/core integer bridge");
  Check(r.HasCheckpoint==false,"null checkpoint bridge");
  Check(r.Bigman==true,"qualified original Mono Dictionary layout resolves");
  Check(((MetaType)qualified.State.Fields[0].Type).ClassReads==0
    &&((MetaType)qualified.State.Fields[1].Type).ClassReads==0,
    "GenericInst Class getter is never invoked");
  r=reader.Read("Subspace_Final");Check(r.Valid&&r.DialoguePlaying==true,"dynamic DialogueManager first-dialogue bridge");
  Check(r.BoundaryMetadataReady&&r.EndingSignals.Contains("ending.gem_dialogue"),"dynamic first GEM payload bridge");
  Check(m.UnboundedCalls==0,"no unbounded Unity string API");Check(m.MaxByteRead==14,"bounded dialogue UTF16 read");
  m=Memory();reader.Configure(m,Metadata());r=reader.Read("Office_1");
  Check(r.Valid&&r.BoundaryMetadataReady&&r.StartReady==true,"dynamic Office_1 control-ready bridge");
  m.Values[46040]=0;r=reader.Read("Office_1");
  Check(r.Valid&&r.BoundaryMetadataReady&&r.StartReady==false,"real _canMove flag false keeps intro unready");
  m.Values[46040]=1;m.Values[4112]=1;r=reader.Read("Office_1");
  Check(r.Valid&&r.StartReady==false,"cutscene still vetoes true _canMove backing flag");
  m.Values[4112]=0;r=reader.Read("Office_1");
  Check(r.Valid&&r.StartReady==true,"control handoff uses real backing field and cleared cutscene");
  var propertyOnly=Metadata();propertyOnly.Player.Fields[0].Name="CanMove";
  var propertyMemory=Memory();var propertyReader=new ReadOnlyReader();propertyReader.Configure(propertyMemory,propertyOnly);
  var propertyResult=propertyReader.Read("Office_1");
  Check(propertyResult.Valid&&!propertyResult.BoundaryMetadataReady&&!propertyResult.StartReady.HasValue
    &&!propertyMemory.ReadAddresses.Contains(46040),"fictional CanMove field cannot stand in for real _canMove metadata");
  var staticBacking=Metadata();staticBacking.Player.Fields[0].IsStatic=true;
  propertyMemory=Memory();propertyReader.Configure(propertyMemory,staticBacking);propertyResult=propertyReader.Read("Office_1");
  Check(propertyResult.Valid&&!propertyResult.StartReady.HasValue&&!propertyMemory.ReadAddresses.Contains(46040),"static _canMove rejected before instance read");
  var wrongBacking=Metadata();wrongBacking.Player.Fields[0].Type=Primitive("I4");
  propertyMemory=Memory();propertyReader.Configure(propertyMemory,wrongBacking);propertyResult=propertyReader.Read("Office_1");
  Check(propertyResult.Valid&&!propertyResult.StartReady.HasValue&&!propertyMemory.ReadAddresses.Contains(46040),"non-Boolean _canMove rejected before remote read");
  DialogueLine(m,"Gem/m14");r=reader.Read("Subspace_Final");
  Check(r.Valid&&r.DialoguePlaying==true&&r.EndingSignals.Count==0,"later dialogue payload rejected by bridge");
  m.Values[36268]=0;DialogueLine(m,"Gem/m11");r=reader.Read("Subspace_Final");
  Check(r.Valid&&r.EndingSignals.Count==0,"zero visible characters rejected by bridge");
  Check(m.UnboundedCalls==0,"no unbounded Unity string API");Check(m.MaxByteRead==14,"bounded dialogue UTF16 read");

  m=Memory();reader.Configure(m,Metadata());
  foreach(string boundaryScene in new string[]{"T_Boss","Office_3","Office_4"}){
   r=reader.Read(boundaryScene);
   Check(r.Valid&&r.BoundaryMetadataReady,"dynamic "+boundaryScene+" boundary metadata ready");
  }
  var noChoices=Metadata();noChoices.State.Fields[0].Type=Primitive("I4");m=Memory();reader.Configure(m,noChoices);r=reader.Read("T_Boss");
  Check(r.Valid&&!r.BoundaryMetadataReady,"dynamic missing choices metadata stays unknown");
  var malformedChoices=Metadata();malformedChoices.Dictionary.Fields[1].Offset=16;m=Memory();reader.Configure(m,malformedChoices);r=reader.Read("Office_3");
  Check(r.Valid&&!r.BoundaryMetadataReady,"dynamic malformed choices metadata fails closed");
  var noCutscene=Metadata();noCutscene.Scene.Fields[2].Name="NoCutscene";m=Memory();reader.Configure(m,noCutscene);r=reader.Read("Office_4");
  Check(r.Valid&&!r.BoundaryMetadataReady,"dynamic missing cutscene metadata stays unknown");
  var unrelatedOnly=Metadata();unrelatedOnly.State.Fields[1].Type=Primitive("I4");unrelatedOnly.Dialogue=null;m=Memory();reader.Configure(m,unrelatedOnly);r=reader.Read("T_Boss");
  Check(r.Valid&&r.BoundaryMetadataReady,"dynamic T_Boss does not require unrelated metadata");
  m=Memory();m.Values.Remove(4112);m.Values.Remove(20496);m.Bytes.Remove(20500);reader.Configure(m,Metadata());r=reader.Read("T_Boss");
  Check(r.Valid&&r.BoundaryMetadataReady,"dynamic T_Boss readiness ignores absent values");
  m=Memory();m.Values[4112]=0;m.Values[16432]=2;reader.Configure(m,Metadata());r=reader.Read("T_Boss");
  Check(r.Valid&&r.BoundaryMetadataReady,"dynamic T_Boss readiness ignores positive value");
  m=Memory();m.Values[4112]=1;m.Values[16432]=0;reader.Configure(m,Metadata());r=reader.Read("T_Boss");
  Check(r.Valid&&r.BoundaryMetadataReady,"dynamic T_Boss readiness ignores loss value");
  m=Memory();reader.Configure(m,Metadata());r=reader.Read("B_5");
  Check(r.Valid&&!r.BoundaryMetadataReady,"dynamic unlisted scene remains unknown");

  m=Memory();m.Values[4112]=1;m.Values[20496]=9;m.Bytes[20500]=Encoding.Unicode.GetBytes("OfficeEnd");m.Values[16432]=2;reader.Configure(m,Metadata());r=reader.Read("Office_3");
  Check(r.Valid&&r.EndingSignals.Contains("ending.execution_black")&&!r.GolfBatteries.HasValue&&!r.GolfCutscenePlaying.HasValue,"dynamic Office_3 ending producer bridge");
  m=Memory();m.Values[4112]=1;m.Values[20496]=9;m.Bytes[20500]=Encoding.Unicode.GetBytes("Batteries");m.Values[16432]=2;reader.Configure(m,Metadata());r=reader.Read("T_Boss");
  m.Values[4112]=0;r=reader.Read("T_Boss");
  Check(r.Valid&&r.GolfBattleActive==true&&r.GolfBatteries==2&&r.GolfCutscenePlaying==false
    &&r.TheodoreMetadataReady&&r.TheodoreHp==100,"dynamic T_Boss active battle and HP bridge");
  m.Values[70312]=1112014848;m.Values[4112]=1;m.Values[16432]=0;r=reader.Read("T_Boss");
  Check(r.Valid&&r.GolfBattleActive==false&&r.GolfBatteries==0&&r.GolfCutscenePlaying==true&&r.EndingSignals.Contains("ending.theodore_death"),"dynamic Theodore loss bridge");
  m.Values[4096]=300;r=reader.Read("T_Boss");
  Check(r.Valid&&r.EndingSignals.Contains("ending.theodore_death"),"dynamic saved Abyss 300 permits replay loss observation");
  m.Values[16432]=2;m.Values[70312]=0;r=reader.Read("T_Boss");
  Check(r.Valid&&r.GolfBattleActive==false&&!r.EndingSignals.Contains("ending.theodore_death")
    &&r.TheodoreHp==0&&r.EndingSignals.Contains("ending.theodore_victory"),"dynamic immediate Theodore victory bridge");
  m=Memory();m.Values[4116]=3;m.Values[8200]=25000;m.Values[25024]=26000;m.Values[25064]=1;m.Values[25068]=0;m.Values[25072]=0;m.Values[25076]=1;m.Values[26024]=1;m.Values[26032]=1;m.Values[26036]=-1;m.Values[26040]=27000;m.Values[26048]=1;m.Values[27016]=4;m.Bytes[27020]=Encoding.Unicode.GetBytes("Dead");reader.Configure(m,Metadata());r=reader.Read("Office_1");
  Check(r.Valid&&r.EndingSignals.Contains("ending.final_death_black"),"dynamic FinalDeath producer bridge");
  r=reader.Read("C_End");Check(r.Valid&&!r.EndingSignals.Contains("ending.final_death_black"),"dynamic C_End entry veto bridge");
  var oldHeader=Metadata();oldHeader.Dictionary.Fields[1].Offset=16;m=Memory();reader.Configure(m,oldHeader);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&r.Diagnostic.IndexOf("choices-metadata-stage-dictionary-shape",StringComparison.Ordinal)>=0
    &&!m.ReadAddresses.Contains(12288L+64),"old 16/24/28 dictionary header is rejected before scan");
  var boxedEntry=Metadata();boxedEntry.Entry.Fields[0].Offset=16;boxedEntry.Entry.Fields[1].Offset=20;boxedEntry.Entry.Fields[2].Offset=24;boxedEntry.Entry.Fields[3].Offset=32;m=Memory();reader.Configure(m,boxedEntry);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&r.Diagnostic.IndexOf("choices-metadata-stage-entry-shape",StringComparison.Ordinal)>=0,"raw boxed Entry metadata is rejected at helper boundary");
  var wrongEntryNs=Metadata();wrongEntryNs.Entry.Namespace="System.Collections.Generic";m=Memory();reader.Configure(m,wrongEntryNs);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&r.Diagnostic.IndexOf("choices-metadata-stage-array-types",StringComparison.Ordinal)>=0,"nested Entry must use actual empty namespace");
  var fakeKeysArray=Metadata();fakeKeysArray.Dictionary.Fields[3].Type=ArrayType(new IntPtr(13002));m=Memory();reader.Configure(m,fakeKeysArray);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&r.Diagnostic.IndexOf("choices-metadata-stage-auxiliary-types",StringComparison.Ordinal)>=0,"KeyCollection is not a fabricated String array");
  var varValue=Metadata();varValue.Entry.Fields[3].Type=Primitive("Var");m=Memory();reader.Configure(m,varValue);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&r.Diagnostic.IndexOf("choices-metadata-stage-entry-shape",StringComparison.Ordinal)>=0,"generic Var value type is rejected");
  var wrongArrayData=Metadata();var wrongEntriesType=wrongArrayData.Dictionary.Fields[1].Type;wrongEntriesType.Data=new IntPtr(13002);wrongEntriesType.PoisonClass=true;m=Memory();reader.Configure(m,wrongArrayData);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&wrongEntriesType.ClassReads==0,"SzArray uses qualified direct Data without Class fallback");
  var genericRace=Metadata();m=Memory();m.GenericClassPointerAddress=14033;m.GenericClassValueAfterFirstRead=13003;reader.Configure(m,genericRace);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&m.GenericClassReads>1
    &&r.Diagnostic.IndexOf("choices-metadata-stage-generic-class",StringComparison.Ordinal)>=0,"cached GenericInst class race fails closed");
  var missingEnding=Metadata();missingEnding.Scene.Fields[2].Name="NoCutscene";missingEnding.Scene.Fields[3].Name="NoDeadState";m=Memory();reader.Configure(m,missingEnding);r=reader.Read("T_Boss");
  Check(r.Valid&&!r.GolfBattleActive.HasValue&&!r.GolfBatteries.HasValue&&!r.GolfCutscenePlaying.HasValue&&r.EndingSignals.Count==0&&r.Diagnostic.IndexOf("ending-cutscene-metadata-unavailable",StringComparison.Ordinal)>=0&&r.Diagnostic.IndexOf("ending-deadstate-metadata-unavailable",StringComparison.Ordinal)>=0,"missing ending metadata stays optional");
  var badEndingOffset=Metadata();badEndingOffset.Scene.Fields[2].Offset=Int32.MaxValue;m=Memory();reader.Configure(m,badEndingOffset);r=reader.Read("T_Boss");
  Check(r.Valid&&!r.GolfBattleActive.HasValue&&!m.ReadAddresses.Contains(4096L+Int32.MaxValue),"absurd ending offset fails closed before remote read");
  m=Memory();m.ChangeLengthAfterCheck=true;reader.Configure(m,Metadata());r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue,"raced string length fails optional marker closed");Check(m.MaxByteRead<=128&&m.UnboundedCalls==0,"race cannot request huge allocation");
  m=Memory();m.Values[4104]=24576;m.Values[24592]=6;m.Bytes[24596]=Encoding.Unicode.GetBytes("Office");reader.Configure(m,Metadata());r=reader.Read("Office_1");
  Check(r.Valid&&r.HasCheckpoint==true,"well-formed checkpoint bridge");
  m=Memory();m.Values[4104]=24576;reader.Configure(m,Metadata());r=reader.Read("Office_1");
  Check(r.Valid&&!r.HasCheckpoint.HasValue&&r.Diagnostic.IndexOf("checkpoint-malformed",StringComparison.Ordinal)>=0,"malformed checkpoint bridge");
  m=Memory();m.CheckpointValueAfterFirstRead=24576;reader.Configure(m,Metadata());r=reader.Read("Office_1");
  Check(r.Valid&&!r.HasCheckpoint.HasValue&&r.Diagnostic.IndexOf("checkpoint-mutated",StringComparison.Ordinal)>=0,"null-to-present checkpoint race bridge");
  m=Memory();m.Values[4104]=24576;m.Values[24592]=6;m.Bytes[24596]=Encoding.Unicode.GetBytes("Office");m.CheckpointValueAfterFirstRead=0;reader.Configure(m,Metadata());r=reader.Read("Office_1");
  Check(r.Valid&&!r.HasCheckpoint.HasValue&&r.Diagnostic.IndexOf("checkpoint-mutated",StringComparison.Ordinal)>=0,"present-to-null checkpoint race bridge");
  var badCore=Metadata();badCore.Scene.Fields[0].Offset=Int32.MaxValue;m=Memory();reader.Configure(m,badCore);r=reader.Read("Office_1");
  Check(!r.Valid&&!m.ReadAddresses.Contains(4096L+Int32.MaxValue),"int.MaxValue core offset bridge fails closed");
  var badCheckpoint=Metadata();badCheckpoint.Scene.Fields[1].Offset=Int32.MaxValue;m=Memory();reader.Configure(m,badCheckpoint);r=reader.Read("Office_1");
  Check(r.Valid&&!r.HasCheckpoint.HasValue&&!m.ReadAddresses.Contains(4096L+Int32.MaxValue),"int.MaxValue checkpoint offset bridge remains unknown");
  var badDictionaryStatic=Metadata();badDictionaryStatic.State.Fields[0].Offset=Int32.MaxValue;m=Memory();reader.Configure(m,badDictionaryStatic);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&!m.ReadAddresses.Contains(8192L+Int32.MaxValue),"int.MaxValue dictionary static offset bridge fails closed");
  var badDictionaryObject=Metadata();m=Memory();m.Values[14033]=Int32.MaxValue;reader.Configure(m,badDictionaryObject);r=reader.Read("B_6.6");
  Check(r.Valid&&!r.Bigman.HasValue&&!m.ReadAddresses.Contains(Int32.MaxValue),"wrong cached dictionary class bridge fails closed");
  m=Memory();m.FailCheckpointAfterFirstRead=true;reader.Configure(m,Metadata());r=reader.Read("Office_1");
  Check(r.Valid&&!r.HasCheckpoint.HasValue&&r.Diagnostic.IndexOf("checkpoint-read-failed",StringComparison.Ordinal)>=0,"second checkpoint pointer read failure bridge");
  var optionalMalformed=Metadata();optionalMalformed.Scene.Fields[1].Type=Primitive("I4");reader.Configure(Memory(),optionalMalformed);r=reader.Read("Office_1");
  Check(r.Valid&&!r.HasCheckpoint.HasValue&&r.Diagnostic.IndexOf("checkpoint-metadata-unavailable",StringComparison.Ordinal)>=0,"bad checkpoint metadata preserves Abyss");
  var badDialogue=Metadata();badDialogue.Dialogue.Fields[1].Type=Primitive("I4");reader.Configure(Memory(),badDialogue);r=reader.Read("Subspace_Final");
  Check(r.Valid&&!r.DialoguePlaying.HasValue&&r.Diagnostic.IndexOf("dialogue-metadata-unavailable",StringComparison.Ordinal)>=0,"bad dialogue metadata preserves core sample");
  var badPlayingOffset=Metadata();badPlayingOffset.Dialogue.Fields[1].Offset=Int32.MaxValue;m=Memory();reader.Configure(m,badPlayingOffset);r=reader.Read("Subspace_Final");
  Check(r.Valid&&!r.DialoguePlaying.HasValue&&!m.ReadAddresses.Contains(28672L+Int32.MaxValue),"absurd dialogue field offset fails closed before remote read");
  var badVisibleOffset=Metadata();badVisibleOffset.TextMeshPro["TMPro.TMP_Text"].Fields[0].Offset=Int32.MaxValue;m=Memory();reader.Configure(m,badVisibleOffset);r=reader.Read("Subspace_Final");
  Check(r.Valid&&!r.BoundaryMetadataReady&&r.EndingSignals.Count==0
    &&r.Diagnostic.IndexOf("dialogue-metadata-stage-tmp-visible-offset",StringComparison.Ordinal)>=0
    &&!m.ReadAddresses.Contains(35000L+Int32.MaxValue),"absurd inherited TMP offset fails closed");
  foreach(int invalidVisibleOffset in new int[]{1264,1272,-4,1266}){
   var invalidVisible=Metadata();invalidVisible.TextMeshPro["TMPro.TMP_Text"].Fields[0].Offset=invalidVisibleOffset;m=Memory();reader.Configure(m,invalidVisible);r=reader.Read("Subspace_Final");
   Check(r.Valid&&!r.BoundaryMetadataReady&&r.EndingSignals.Count==0
     &&r.Diagnostic.IndexOf("dialogue-metadata-stage-tmp-visible-offset",StringComparison.Ordinal)>=0
     &&!m.ReadAddresses.Contains(35000L+invalidVisibleOffset),"nonqualified inherited TMP offset fails closed: "+invalidVisibleOffset);
  }
  var badStartOffset=Metadata();badStartOffset.Scene.Fields[2].Offset=Int32.MaxValue;m=Memory();reader.Configure(m,badStartOffset);r=reader.Read("Office_1");
  Check(r.Valid&&!r.BoundaryMetadataReady&&!m.ReadAddresses.Contains(4096L+Int32.MaxValue),"absurd start metadata offset fails closed");
  var badCurrentType=Metadata();badCurrentType.Dialogue.Fields[3].Type=Primitive("Boolean");m=Memory();reader.Configure(m,badCurrentType);r=reader.Read("Subspace_Final");
  Check(r.Valid&&!r.BoundaryMetadataReady&&r.EndingSignals.Count==0,"CurrentLineIndex type failure preserves core");
  m=Memory();reader.Configure(m,Metadata());Action<MemoryHelper,long> lengthRace=null;
  lengthRace=delegate(MemoryHelper x,long address){if(address==37016)x.Values[37016]=1000000;else x.AfterRead=lengthRace;};m.AfterRead=lengthRace;r=reader.Read("Subspace_Final");
  Check(r.Valid&&r.EndingSignals.Count==0&&m.MaxByteRead<=14,"dialogue string length race remains bounded");
  var badVisibleType=Metadata();badVisibleType.TextMeshPro["TMPro.TMP_Text"].Fields[0].Type=Primitive("Boolean");m=Memory();reader.Configure(m,badVisibleType);r=reader.Read("Subspace_Final");
  Check(r.Valid&&!r.BoundaryMetadataReady&&r.EndingSignals.Count==0,"inherited TMP visibility type failure preserves core");
  var wrongTextBox=Metadata();wrongTextBox.Dialogue.Fields[4].Type.Class=new MetaClass{Name="TextMeshProUGUI",Namespace="TMPro",Address=new IntPtr(9999)};m=Memory();reader.Configure(m,wrongTextBox);r=reader.Read("Subspace_Final");
  Check(r.Valid&&!r.BoundaryMetadataReady&&r.EndingSignals.Count==0,"TextMeshProUGUI class identity mismatch preserves core");
  var badArrayType=Metadata();badArrayType.Dialogue.Fields[2].Type=Primitive("I4");m=Memory();reader.Configure(m,badArrayType);r=reader.Read("Subspace_Final");
  Check(r.Valid&&!r.BoundaryMetadataReady&&r.EndingSignals.Count==0,"DILines type failure preserves core");
  var badDictionary=Metadata();badDictionary.State.Fields[0].Type=Primitive("I4");m=Memory();reader.Configure(m,badDictionary);r=reader.Read("Subspace_Final");
  Check(r.Valid&&r.BoundaryMetadataReady&&r.EndingSignals.Contains("ending.gem_dialogue"),"dictionary failure does not strand dialogue metadata");
  m=Memory();reader.Configure(m,Metadata());Action<MemoryHelper,long> pointerRace=null;
  pointerRace=delegate(MemoryHelper x,long address){if(address==24576)x.Values[24576]=29999;else x.AfterRead=pointerRace;};m.AfterRead=pointerRace;r=reader.Read("Subspace_Final");
  Check(r.Valid&&r.EndingSignals.Count==0&&r.Diagnostic.IndexOf("dialogue-mutated",StringComparison.Ordinal)>=0,"dialogue singleton pointer race bridge");
  m=Memory();reader.Configure(m,Metadata());Action<MemoryHelper,long> arrayRace=null;
  arrayRace=delegate(MemoryHelper x,long address){if(address==36024)x.Values[36024]=4097;else x.AfterRead=arrayRace;};m.AfterRead=arrayRace;r=reader.Read("Subspace_Final");
  Check(r.Valid&&r.EndingSignals.Count==0&&r.Diagnostic.IndexOf("dialogue-mutated",StringComparison.Ordinal)>=0,"managed array length race bridge");
  m=Memory();reader.Configure(m,Metadata());Action<MemoryHelper,long> stringRace=null;
  stringRace=delegate(MemoryHelper x,long address){if(address==37020)DialogueLine(x,"Gem/m14");else x.AfterRead=stringRace;};m.AfterRead=stringRace;r=reader.Read("Subspace_Final");
  Check(r.Valid&&r.EndingSignals.Count==0&&r.Diagnostic.IndexOf("dialogue-mutated",StringComparison.Ordinal)>=0,"managed string payload race bridge");
  m=Memory();reader.Configure(m,Metadata());Action<MemoryHelper,long> visibleRace=null;
  visibleRace=delegate(MemoryHelper x,long address){if(address==36268)x.Values[36268]=0;else x.AfterRead=visibleRace;};m.AfterRead=visibleRace;r=reader.Read("Subspace_Final");
  Check(r.Valid&&r.EndingSignals.Count==0&&r.Diagnostic.IndexOf("dialogue-mutated",StringComparison.Ordinal)>=0,"TMP visibility race bridge");
  m=Memory();reader.Configure(m,Metadata());r=reader.Read("B_End");
  Check(r.Valid&&r.BoundaryMetadataReady&&r.KarminaMetadataReady&&r.EndingSignals.Count==0,"B_End shared choice metadata is boundary-ready without a choice");
  var malformed=Metadata();malformed.Scene.Fields[0].Name="Wrong";reader.Configure(Memory(),malformed);Check(!reader.Read("Office_1").Valid,"missing core metadata invalid");
  var cold=Metadata();cold.Scene.Static=IntPtr.Zero;reader.Configure(Memory(),cold);Check(!reader.Read("Office_1").Valid,"zero static storage waits for initialization");
  Console.WriteLine("Reader bridge assertions="+assertions+" failures="+failures+" (offline fixtures)");return failures==0?0:1;
 }
}
