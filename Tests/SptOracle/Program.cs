using System.Runtime.InteropServices;
using System.Globalization;
unsafe class P {
 const string D="speedtreert.dll";
 [DllImport(D,EntryPoint="??0CSpeedTreeRT@@QAE@XZ",CallingConvention=CallingConvention.ThisCall)] static extern IntPtr Ctor(IntPtr t);
 [DllImport(D,EntryPoint="?LoadTree@CSpeedTreeRT@@QAE_NPBEI@Z",CallingConvention=CallingConvention.ThisCall)] static extern byte Load(IntPtr t, byte* b, uint n);
 [DllImport(D,EntryPoint="?Compute@CSpeedTreeRT@@QAE_NPBMI_N@Z",CallingConvention=CallingConvention.ThisCall)] static extern byte Compute(IntPtr t, IntPtr m, uint seed, byte comp);
 [DllImport(D,EntryPoint="?GetTreeSize@CSpeedTreeRT@@QBEXAAM0@Z",CallingConvention=CallingConvention.ThisCall)] static extern void GetSize(IntPtr t, float* s, float* v);
 [DllImport(D,EntryPoint="?SetTreeSize@CSpeedTreeRT@@QAEXMM@Z",CallingConvention=CallingConvention.ThisCall)] static extern void SetSize(IntPtr t, float s, float v);
 [DllImport(D,EntryPoint="?SetLodLevel@CSpeedTreeRT@@QAEXM@Z",CallingConvention=CallingConvention.ThisCall)] static extern void Lod(IntPtr t, float l);
 [DllImport(D,EntryPoint="??0SGeometry@CSpeedTreeRT@@QAE@XZ",CallingConvention=CallingConvention.ThisCall)] static extern IntPtr GCtor(IntPtr g);
 [DllImport(D,EntryPoint="?GetGeometry@CSpeedTreeRT@@QAEXAAUSGeometry@1@KFFF@Z",CallingConvention=CallingConvention.ThisCall)] static extern void Geo(IntPtr t, IntPtr g, uint bits, short a, short b, short c);
 static void Main(string[] a){
  if(a[0]=="--dump"){ Directory.CreateDirectory(a[2]);
   foreach(var f in Directory.GetFiles(a[1],"*.spt",SearchOption.AllDirectories)){
    var data=File.ReadAllBytes(f);
    var t=Marshal.AllocHGlobal(1<<20); new Span<byte>((void*)t,1<<20).Clear(); Ctor(t);
    byte ok; fixed(byte* p=data) ok=Load(t,p,(uint)data.Length); if(ok==0) continue;
    float s,v; GetSize(t,&s,&v); SetSize(t,s*0.3048f,0);
    if(Compute(t,IntPtr.Zero,1,1)==0) continue;
    Lod(t,1f);
    var g=Marshal.AllocHGlobal(4096); new Span<byte>((void*)g,4096).Clear(); GCtor(g); Geo(t,g,1|2|4,-1,-1,-1);
    using var o4=new StreamWriter(Path.Combine(a[2],Path.GetFileName(f).ToLowerInvariant()+".txt"));
    Dump(o4,"B",g,0); Dump(o4,"F",g,60); DumpLeaves(o4,g);
   }
   return; }
  if(a[0]=="--lod"){ using var o3=new StreamWriter(a[2]);
   foreach(var f in Directory.GetFiles(a[1],"*.spt",SearchOption.AllDirectories)){
    var data=File.ReadAllBytes(f);
    var t=Marshal.AllocHGlobal(1<<20); new Span<byte>((void*)t,1<<20).Clear(); Ctor(t);
    byte ok; fixed(byte* p=data) ok=Load(t,p,(uint)data.Length); if(ok==0) continue;
    byte* e=*(byte**)t;
    var fr=*(byte**)((byte*)t+0x58);
    o3.WriteLine(FormattableString.Invariant($"{Path.GetFileName(f).ToLowerInvariant()}\tleaflods {*(int*)(e+0x98)} adj {*(float*)((byte*)t+0x24)} lods {*(int*)(e+0x5c)} max {*(float*)(e+0xb4)} min {*(float*)(e+0xb8)} bc {*(float*)(e+0xbc)} fuzz {*(float*)(e+0xc0)} ret {*(float*)(e+0xc4)} | frond lods {*(int*)(fr+0x44)} max {*(float*)(fr+0x48)} min {*(float*)(fr+0x4c)} fuzz {*(float*)(fr+0x50)} ret {*(float*)(fr+0x54)} type {*(int*)(fr+0x20)} blades {*(int*)(fr+0x24)}"));
   }
   return; }
  if(a[0]=="--stats"){ using var o2=new StreamWriter(a[2]);
   foreach(var f in Directory.GetFiles(a[1],"*.spt",SearchOption.AllDirectories)){
    var data=File.ReadAllBytes(f);
    var t=Marshal.AllocHGlobal(1<<20); new Span<byte>((void*)t,1<<20).Clear(); Ctor(t);
    byte ok; fixed(byte* p=data) ok=Load(t,p,(uint)data.Length); if(ok==0) continue;
    float s,v; GetSize(t,&s,&v); SetSize(t,s*0.3048f,0);
    if(Compute(t,IntPtr.Zero,1,1)==0) continue;
    Lod(t,1f);
    var g=Marshal.AllocHGlobal(4096); new Span<byte>((void*)g,4096).Clear(); GCtor(g); Geo(t,g,1|2|4,-1,-1,-1);
    byte* b=(byte*)g; var z=new List<float>(); var r=new List<float>();
    foreach(int off in new[]{0,60}){ byte* q=b+off; ushort vc=*(ushort*)(q+16); float* c=*(float**)(q+36); if(c==null) continue;
     for(int i=0;i<vc;i++){ z.Add(c[i*3+2]); r.Add(MathF.Sqrt(c[i*3]*c[i*3]+c[i*3+1]*c[i*3+1])); } }
    if(z.Count==0) continue; z.Sort(); r.Sort();
    o2.WriteLine(FormattableString.Invariant($"{Path.GetFileName(f).ToLowerInvariant()}\t{z[0]:0.00}\t{z[z.Count/2]:0.00}\t{z[^1]:0.00}\t{r[r.Count/2]:0.00}\t{r[^1]:0.00}"));
   }
   return; }
  if(a[0].EndsWith(".spt")){ using var o1=new StreamWriter(a[1]); var d1=File.ReadAllBytes(a[0]); var t1=Marshal.AllocHGlobal(1<<20); new Span<byte>((void*)t1,1<<20).Clear(); Ctor(t1); fixed(byte* p1=d1) Load(t1,p1,(uint)d1.Length); float s1,v1; GetSize(t1,&s1,&v1); SetSize(t1,s1*0.3048f,0); Compute(t1,IntPtr.Zero,1,1); Lod(t1,1f); var g1=Marshal.AllocHGlobal(4096); new Span<byte>((void*)g1,4096).Clear(); GCtor(g1); Geo(t1,g1,1|2|4,-1,-1,-1); Dump(o1,"B",g1,0); Dump(o1,"F",g1,60); return; }
  using var o=new StreamWriter(a[1]);
  foreach(var f in Directory.GetFiles(a[0],"*.spt",SearchOption.AllDirectories)){
   var data=File.ReadAllBytes(f);
   var t=Marshal.AllocHGlobal(1<<20); new Span<byte>((void*)t,1<<20).Clear(); Ctor(t);
   byte ok; fixed(byte* p=data) ok=Load(t,p,(uint)data.Length);
   if(ok==0){o.WriteLine("ERRO	"+Path.GetFileName(f));continue;}
   float s,v; GetSize(t,&s,&v); SetSize(t,s*0.3048f,0);
   if(Compute(t,IntPtr.Zero,1,1)==0){o.WriteLine("ERRO2	"+Path.GetFileName(f));continue;}
   Lod(t,1f);
   var g=Marshal.AllocHGlobal(4096); new Span<byte>((void*)g,4096).Clear(); GCtor(g); Geo(t,g,1|2|4,-1,-1,-1);
   byte* b=(byte*)g; ushort bv=*(ushort*)(b+16), fv=*(ushort*)(b+60+16);
   float top=0; float* c=*(float**)(b+36); for(int i=0;i<bv;i++) top=Math.Max(top,c[i*3+2]);
   float* c2=*(float**)(b+60+36); if(c2!=null) for(int i=0;i<fv;i++) top=Math.Max(top,c2[i*3+2]);
   o.WriteLine(FormattableString.Invariant($"{Path.GetFileName(f).ToLowerInvariant()}	{bv}	{fv}	{top:0.0}"));
  }
 }
 static void DumpLeaves(StreamWriter o,IntPtr g){ byte* b=(byte*)g+120; ushort n=*(ushort*)(b+12); float* c=*(float**)(b+24); if(c==null) return; for(int i=0;i<n;i++) o.WriteLine(FormattableString.Invariant($"v L {c[i*3]} {c[i*3+1]} {c[i*3+2]}")); }
 static int[] Used(byte* b){ ushort strips=*(ushort*)(b+4); ushort** st=*(ushort***)(b+12); ushort* len=*(ushort**)(b+8); var u=new SortedSet<int>(); for(int k=0;k<strips;k++) for(int i=0;i<len[k];i++) u.Add(st[k][i]); return u.ToArray(); }
 static void Dump(StreamWriter o,string tag,IntPtr g,int off){
  byte* b=(byte*)g+off; float* c=*(float**)(b+36);
  if(c==null) return;
  foreach(int i in Used(b)) o.WriteLine(FormattableString.Invariant($"v {tag} {c[i*3]} {c[i*3+1]} {c[i*3+2]}"));
 }
}
