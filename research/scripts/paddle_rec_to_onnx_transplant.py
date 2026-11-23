"""Paddle PP-OCRv5 recognition model (PIR .json + .pdiparams) -> ONNX, WITHOUT paddle2onnx.

Why: paddle2onnx 2.x does not import against paddlepaddle 3.3.1 on Windows (its extension needs PIR
symbols libpaddle.pyd does not export), and Pine's exact weights (Sdcb.PaddleOCR.Models.LocalV5
latin_PP-OCRv5_mobile_rec: 836-char dictionary, 838 CTC classes) have no published ONNX build.
RapidOCR's latin_PP-OCRv5_rec_mobile.onnx has the SAME graph and parameter names but different weights
and a 504-class head, so its graph is kept and the Paddle weights are poured in:
  * every initializer named like a Paddle parameter is replaced by the Paddle tensor;
  * conv biases (p2o.pd_op.reshape.N.0, [1,C,1,1]) are mapped through the Add->Conv they feed;
  * layer-norm scale/bias (helper.reshape.N) are mapped through the program order of PIR layer_norm ops;
  * every 504 in the output / value_info shapes becomes 838.
Raw protobuf editing; numpy only. Verified in docs/65: identical text to Paddle inference on 98.3% of
603 lines (CER 0.07%). The detection model needs no conversion: RapidOCR's ch_PP-OCRv5_det_mobile.onnx
carries weights bit-identical to Sdcb's mobile_zh_det (178/178 shared parameters equal).
Usage: python paddle_rec_to_onnx_transplant.py <paddle_model_dir> <donor_rapidocr_rec.onnx> <out.onnx>
"""
import sys, collections
import numpy as np
def varint(b,i):
    r=0;s=0
    while True:
        c=b[i];i+=1;r|=(c&0x7f)<<s;s+=7
        if c<0x80:return r,i
def fields(b):
    i=0
    while i<len(b):
        k,i=varint(b,i);f,w=k>>3,k&7
        if w==0:v,i=varint(b,i)
        elif w==2:
            l,i=varint(b,i);v=b[i:i+l];i+=l
        elif w==1:v=b[i:i+8];i+=8
        elif w==5:v=b[i:i+4];i+=4
        else: raise ValueError(w)
        yield f,w,v
def enc_varint(v):
    o=bytearray()
    while True:
        c=v&0x7f;v>>=7
        if v:o.append(c|0x80)
        else:o.append(c);return bytes(o)
def enc(f,w,v):
    k=enc_varint((f<<3)|w)
    if w==0:return k+enc_varint(v)
    if w==2:return k+enc_varint(len(v))+v
    return k+v
def tensor_info(t):
    dims=[];name=None;raw=None;dt=None;fl=[]
    for f,w,v in fields(t):
        if f==1:dims.append(v)
        elif f==2:dt=v
        elif f==8:name=v.decode()
        elif f==9:raw=v
        elif f==4:fl.append(v)
    return name,dims,dt,raw
def initializers(path):
    b=open(path,'rb').read()
    g=[v for f,w,v in fields(b) if f==7][0]
    out={}
    for f,w,v in fields(g):
        if f==5:
            n,d,dt,raw=tensor_info(v)
            if raw is not None and dt==1:out[n]=np.frombuffer(raw,np.float32).reshape(d)
    return out

import struct, json
DT={0:np.bool_,1:np.int16,2:np.int32,3:np.int64,4:np.float16,5:np.float32,6:np.float64,20:np.uint8,21:np.int8}
def read_combined(path):
    b=open(path,'rb').read();i=0;out=[]
    while i<len(b):
        ver,=struct.unpack_from('<I',b,i);i+=4
        lod,=struct.unpack_from('<Q',b,i);i+=8
        for _ in range(lod):
            n,=struct.unpack_from('<Q',b,i);i+=8+n
        tv,=struct.unpack_from('<I',b,i);i+=4
        ds,=struct.unpack_from('<i',b,i);i+=4
        desc=b[i:i+ds];i+=ds
        # TensorDesc: field1 data_type (varint), field2 dims (repeated varint, maybe packed)
        j=0;dt=None;dims=[]
        while j<len(desc):
            k=desc[j];j+=1;f=k>>3;w=k&7
            if w==0:
                v=0;s=0
                while True:
                    c=desc[j];j+=1;v|=(c&0x7f)<<s;s+=7
                    if c<0x80:break
                if f==1:dt=v
                elif f==2:dims.append(v)
            else:
                l=0;s=0
                while True:
                    c=desc[j];j+=1;l|=(c&0x7f)<<s;s+=7
                    if c<0x80:break
                seg=desc[j:j+l];j+=l
                if f==2:
                    q=0
                    while q<len(seg):
                        v=0;s=0
                        while True:
                            c=seg[q];q+=1;v|=(c&0x7f)<<s;s+=7
                            if c<0x80:break
                        dims.append(v)
        n=int(np.prod(dims)) if dims else 1
        dt_=np.dtype(DT[dt]);nb=n*dt_.itemsize
        arr=np.frombuffer(b[i:i+nb],dtype=dt_).reshape(dims).copy();i+=nb
        out.append(arr)
    return out
def pir_params(jpath):
    d=json.load(open(jpath,encoding='utf8'))
    ops=d['program']['regions'][0]['blocks'][0]['ops']
    res=[]
    for o in ops:
        if o['#']=='p':
            res.append((o['A'][3],o['O']['TT']['D'][1], o['A'][:3], o['OA']))
    return res

src=sys.argv[2]; base=sys.argv[1]
ps=sorted(pir_params(base+'/inference.json'),key=lambda p:p[0]); ar=read_combined(base+'/inference.pdiparams')
P={p[0]:a for p,a in zip(ps,ar)}
model=open(src,'rb').read()
mf=list(fields(model)); gi=[i for i,(f,w,v) in enumerate(mf) if f==7][0]
gf=list(fields(mf[gi][2]))
nodes=[]; inits={}
for f,w,v in gf:
    if f==1:
        n=dict(inp=[],out=[],op=None)
        for ff,ww,vv in fields(v):
            if ff==1:n['inp'].append(vv.decode())
            elif ff==2:n['out'].append(vv.decode())
            elif ff==4:n['op']=vv.decode()
        nodes.append(n)
    elif f==5:
        nm,d,dt,raw=tensor_info(v); inits[nm]=(d,dt)
prod={o:n for n in nodes for o in n['out']}
cons=collections.defaultdict(list)
for n in nodes:
    for i in n['inp']:cons[i].append(n)
newname={}   # onnx initializer name -> paddle param name
# conv biases
for nm in inits:
    if nm.startswith('p2o.pd_op.reshape'):
        add=[c for c in cons[nm] if c['op']=='Add']; assert len(add)==1,(nm,[c['op'] for c in cons[nm]])
        other=[i for i in add[0]['inp'] if i!=nm][0]; conv=prod[other]; assert conv['op']=='Conv',conv['op']
        wname=conv['inp'][1]; assert wname.endswith('.w_0'); newname[nm]=wname[:-4]+'.b_0'
# layer norms: program order of layer_norm ops in PIR json
d=json.load(open(base+'/inference.json',encoding='utf8')); ops=d['program']['regions'][0]['blocks'][0]['ops']
vid={}
for o in ops:
    if o['#']=='p': vid[o['O']['%']]=o['A'][3]
ln=[]
for o in ops:
    if o['#']=='1.layer_norm':
        ids=[x['%'] for x in o['I']]; ln.append([vid[i] for i in ids if i in vid])
print('layer_norm ops',ln)
hr=sorted([n for n in inits if n.startswith('helper.reshape.')],key=lambda s:int(s.split('.')[-1]))
# helper.reshape order in graph: for each LN, gamma consumed by Mul then beta by Add
def topo_index(nm): return min(nodes.index(c) for c in cons[nm])
hr.sort(key=topo_index)
for k,lnp in enumerate(ln):
    g,b=hr[2*k],hr[2*k+1]
    cg=cons[g][0]['op'];cb=cons[b][0]['op']; assert (cg,cb)==('Mul','Add'),(cg,cb)
    wn=[p for p in lnp if p.endswith('.w_0')][0];bn=[p for p in lnp if p.endswith('.b_0')][0]
    newname[g]=wn;newname[b]=bn
print('mapped extra',len(newname))
out=[];rep=0
for f,w,v in gf:
    if f==5:
        nm,dims,dt,raw=tensor_info(v)
        key=newname.get(nm,nm)
        if key in P and dt==1:
            a=P[key].astype(np.float32)
            if nm in newname: a=a.reshape(dims)   # [1,C,1,1] / (120,) layouts
            body=b''.join(enc(1,0,x) for x in a.shape)+enc(2,0,1)+enc(8,2,nm.encode())+enc(9,2,a.tobytes())
            out.append(enc(5,2,body));rep+=1;continue
    elif f in (12,13):
        def patch(buf,depth):
            o=[]
            for ff,ww,vv in fields(buf):
                if ww==2 and depth<4 and ff in (2,1) and not (depth==0 and ff==1): vv=patch(vv,depth+1)
                if depth==3 and ff==1 and ww==0 and vv==504: vv=838
                o.append(enc(ff,ww,vv))
            return b''.join(o)
        # ValueInfo: type(2) -> tensor_type(1) -> shape(2) -> dim(1) -> dim_value(1)
        def patch2(buf,path):
            o=[]
            for ff,ww,vv in fields(buf):
                if path and ff==path[0] and ww==2: vv=patch2(vv,path[1:])
                elif not path and ff==1 and ww==0 and vv==504: vv=838
                o.append(enc(ff,ww,vv))
            return b''.join(o)
        out.append(enc(f,2,patch2(v,[2,1,2,1])));continue
    out.append(enc(f,w,v))
print('replaced',rep,'of',len(inits))
g2=b''.join(out)
# patch output dim 504 -> 838 in graph.output value infos (field 12)
g2=g2.replace(bytes([0x10])+enc_varint(504),bytes([0x10])+enc_varint(838)) if False else g2
res=b''.join(enc(f,w,v) if i!=gi else enc(7,2,g2) for i,(f,w,v) in enumerate(mf))
open(sys.argv[3],'wb').write(res)
