#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <string>
#include <algorithm>
struct Bits {
 const uint8_t* d; int n,p=0;
 int read(int k) { if(k<0||k>24||k>n*8-p)throw std::runtime_error("MPPC bitstream truncated"); int v=0; for(int i=0;i<k;++i,++p)v=(v<<1)|((d[p/8]>>(7-p%8))&1);return v; }
 int peek(int k){int old=p,v=read(k);p=old;return v;}
 void align(){int k=(8-p%8)%8;if(read(k))throw std::runtime_error("nonzero padding");}
};
static void packet(const uint8_t* d,int n,uint8_t* o,int sz){
 Bits b{d,n};int at=0,hist=0;
 while(at<sz){
  if(b.read(1)==0){o[at++]=b.read(7);continue;}
  if(b.read(1)==0){o[at++]=0x80|b.read(7);continue;}
  int dist=b.read(1)==0?320+b.read(13):b.read(1)==0?64+b.read(8):b.read(6);
  if(dist==0){b.align();hist=at;continue;}
  int ones=0;while(b.read(1)!=0)if(++ones>11)throw std::runtime_error("length prefix");
  int len=ones==0?3:(1<<(ones+1))+b.read(ones+1);
  if(dist>8191||dist>at-hist||len>sz-at)throw std::runtime_error("invalid history/output reference");
  for(int j=0;j<len;j++,at++)o[at]=o[at-dist];
 }
 if(n*8-b.p>=10&&b.peek(10)==0x3c0){b.read(10);b.align();}
 if(n*8-b.p>31)throw std::runtime_error("trailing bits");
 while(b.p<n*8)if(b.read(1))throw std::runtime_error("trailing nonzero bits");
}
static int framed(const uint8_t*d,int n,uint8_t*o,int sz){
 int at=0,p=0,raw=0;
 while(at<sz){
  if(n-p<2)throw std::runtime_error("frame header truncated");
  int h=d[p]|d[p+1]<<8;p+=2;int k=h&0x7fff,want=std::min(8192,sz-at);
  if(k==0||k>n-p)throw std::runtime_error("invalid frame size");
  if(h&0x8000)packet(d+p,k,o+at,want);
  else{if(k!=want)throw std::runtime_error("raw frame size mismatch");memcpy(o+at,d+p,k);raw++;}
  p+=k;at+=want;
 }
 if(p!=n)throw std::runtime_error("trailing framed bytes");
 return raw;
}
extern "C" int mppc(const uint8_t*d,int n,uint8_t*o,int sz,char*err,int errlen){
 try{
  if(sz>8192){try{return 1+framed(d,n,o,sz)*2;}catch(const std::runtime_error&){packet(d,n,o,sz);return 0;}}
  packet(d,n,o,sz);return 0;
 }catch(const std::exception&e){std::strncpy(err,e.what(),errlen-1);return -1;}
}
