!function(t,e){"object"==typeof exports&&"object"==typeof module?module.exports=e():"function"==typeof define&&define.amd?define([],e):"object"==typeof exports?exports.MobileOnly=e():t.MobileOnly=e()}(self,(function(){return(()=>{var t={805:t=>{"use strict";t.exports=n,t.exports.isMobile=n,t.exports.default=n;const e=/(android|bb\d+|meego).+mobile|armv7l|avantgo|bada\/|blackberry|blazer|compal|elaine|fennec|hiptop|iemobile|ip(hone|od)|iris|kindle|lge |maemo|midp|mmp|mobile.+firefox|netfront|opera m(ob|in)i|palm( os)?|phone|p(ixi|re)\/|plucker|pocket|psp|series[46]0|symbian|treo|up\.(browser|link)|vodafone|wap|windows (ce|phone)|xda|xiino/i,r=/android|ipad|playbook|silk/i;function n(t){t||(t={});let n=t.ua;if(n||"undefined"==typeof navigator||(n=navigator.userAgent),n&&n.headers&&"string"==typeof n.headers["user-agent"]&&(n=n.headers["user-agent"]),"string"!=typeof n)return!1;let o=e.test(n)||!!t.tablet&&r.test(n);return!o&&t.tablet&&t.featureDetect&&navigator&&navigator.maxTouchPoints>1&&-1!==n.indexOf("Macintosh")&&-1!==n.indexOf("Safari")&&(o=!0),o}},304:(t,e,r)=>{function n(t){this.mode=i.MODE_8BIT_BYTE,this.data=t,this.parsedData=[];for(var e=0,r=this.data.length;e<r;e++){var n=[],o=this.data.charCodeAt(e);o>65536?(n[0]=240|(1835008&o)>>>18,n[1]=128|(258048&o)>>>12,n[2]=128|(4032&o)>>>6,n[3]=128|63&o):o>2048?(n[0]=224|(61440&o)>>>12,n[1]=128|(4032&o)>>>6,n[2]=128|63&o):o>128?(n[0]=192|(1984&o)>>>6,n[1]=128|63&o):n[0]=o,this.parsedData.push(n)}this.parsedData=Array.prototype.concat.apply([],this.parsedData),this.parsedData.length!=this.data.length&&(this.parsedData.unshift(191),this.parsedData.unshift(187),this.parsedData.unshift(239))}function o(t,e){this.typeNumber=t,this.errorCorrectLevel=e,this.modules=null,this.moduleCount=0,this.dataCache=null,this.dataList=[]}n.prototype={getLength:function(t){return this.parsedData.length},write:function(t){for(var e=0,r=this.parsedData.length;e<r;e++)t.put(this.parsedData[e],8)}},o.prototype={addData:function(t){var e=new n(t);this.dataList.push(e),this.dataCache=null},isDark:function(t,e){if(t<0||this.moduleCount<=t||e<0||this.moduleCount<=e)throw new Error(t+","+e);return this.modules[t][e]},getModuleCount:function(){return this.moduleCount},make:function(){this.makeImpl(!1,this.getBestMaskPattern())},makeImpl:function(t,e){this.moduleCount=4*this.typeNumber+17,this.modules=new Array(this.moduleCount);for(var r=0;r<this.moduleCount;r++){this.modules[r]=new Array(this.moduleCount);for(var n=0;n<this.moduleCount;n++)this.modules[r][n]=null}this.setupPositionProbePattern(0,0),this.setupPositionProbePattern(this.moduleCount-7,0),this.setupPositionProbePattern(0,this.moduleCount-7),this.setupPositionAdjustPattern(),this.setupTimingPattern(),this.setupTypeInfo(t,e),this.typeNumber>=7&&this.setupTypeNumber(t),null==this.dataCache&&(this.dataCache=o.createData(this.typeNumber,this.errorCorrectLevel,this.dataList)),this.mapData(this.dataCache,e)},setupPositionProbePattern:function(t,e){for(var r=-1;r<=7;r++)if(!(t+r<=-1||this.moduleCount<=t+r))for(var n=-1;n<=7;n++)e+n<=-1||this.moduleCount<=e+n||(this.modules[t+r][e+n]=0<=r&&r<=6&&(0==n||6==n)||0<=n&&n<=6&&(0==r||6==r)||2<=r&&r<=4&&2<=n&&n<=4)},getBestMaskPattern:function(){for(var t=0,e=0,r=0;r<8;r++){this.makeImpl(!0,r);var n=s.getLostPoint(this);(0==r||t>n)&&(t=n,e=r)}return e},createMovieClip:function(t,e,r){var n=t.createEmptyMovieClip(e,r);this.make();for(var o=0;o<this.modules.length;o++)for(var i=1*o,s=0;s<this.modules[o].length;s++){var a=1*s;this.modules[o][s]&&(n.beginFill(0,100),n.moveTo(a,i),n.lineTo(a+1,i),n.lineTo(a+1,i+1),n.lineTo(a,i+1),n.endFill())}return n},setupTimingPattern:function(){for(var t=8;t<this.moduleCount-8;t++)null==this.modules[t][6]&&(this.modules[t][6]=t%2==0);for(var e=8;e<this.moduleCount-8;e++)null==this.modules[6][e]&&(this.modules[6][e]=e%2==0)},setupPositionAdjustPattern:function(){for(var t=s.getPatternPosition(this.typeNumber),e=0;e<t.length;e++)for(var r=0;r<t.length;r++){var n=t[e],o=t[r];if(null==this.modules[n][o])for(var i=-2;i<=2;i++)for(var a=-2;a<=2;a++)this.modules[n+i][o+a]=-2==i||2==i||-2==a||2==a||0==i&&0==a}},setupTypeNumber:function(t){for(var e=s.getBCHTypeNumber(this.typeNumber),r=0;r<18;r++){var n=!t&&1==(e>>r&1);this.modules[Math.floor(r/3)][r%3+this.moduleCount-8-3]=n}for(r=0;r<18;r++)n=!t&&1==(e>>r&1),this.modules[r%3+this.moduleCount-8-3][Math.floor(r/3)]=n},setupTypeInfo:function(t,e){for(var r=this.errorCorrectLevel<<3|e,n=s.getBCHTypeInfo(r),o=0;o<15;o++){var i=!t&&1==(n>>o&1);o<6?this.modules[o][8]=i:o<8?this.modules[o+1][8]=i:this.modules[this.moduleCount-15+o][8]=i}for(o=0;o<15;o++)i=!t&&1==(n>>o&1),o<8?this.modules[8][this.moduleCount-o-1]=i:o<9?this.modules[8][15-o-1+1]=i:this.modules[8][15-o-1]=i;this.modules[this.moduleCount-8][8]=!t},mapData:function(t,e){for(var r=-1,n=this.moduleCount-1,o=7,i=0,a=this.moduleCount-1;a>0;a-=2)for(6==a&&a--;;){for(var u=0;u<2;u++)if(null==this.modules[n][a-u]){var h=!1;i<t.length&&(h=1==(t[i]>>>o&1)),s.getMask(e,n,a-u)&&(h=!h),this.modules[n][a-u]=h,-1==--o&&(i++,o=7)}if((n+=r)<0||this.moduleCount<=n){n-=r,r=-r;break}}}},o.PAD0=236,o.PAD1=17,o.createData=function(t,e,r){for(var n=l.getRSBlocks(t,e),i=new d,a=0;a<r.length;a++){var u=r[a];i.put(u.mode,4),i.put(u.getLength(),s.getLengthInBits(u.mode,t)),u.write(i)}var h=0;for(a=0;a<n.length;a++)h+=n[a].dataCount;if(i.getLengthInBits()>8*h)throw new Error("code length overflow. ("+i.getLengthInBits()+">"+8*h+")");for(i.getLengthInBits()+4<=8*h&&i.put(0,4);i.getLengthInBits()%8!=0;)i.putBit(!1);for(;!(i.getLengthInBits()>=8*h||(i.put(o.PAD0,8),i.getLengthInBits()>=8*h));)i.put(o.PAD1,8);return o.createBytes(i,n)},o.createBytes=function(t,e){for(var r=0,n=0,o=0,i=new Array(e.length),a=new Array(e.length),u=0;u<e.length;u++){var l=e[u].dataCount,d=e[u].totalCount-l;n=Math.max(n,l),o=Math.max(o,d),i[u]=new Array(l);for(var f=0;f<i[u].length;f++)i[u][f]=255&t.buffer[f+r];r+=l;var g=s.getErrorCorrectPolynomial(d),c=new h(i[u],g.getLength()-1).mod(g);for(a[u]=new Array(g.getLength()-1),f=0;f<a[u].length;f++){var p=f+c.getLength()-a[u].length;a[u][f]=p>=0?c.get(p):0}}var v=0;for(f=0;f<e.length;f++)v+=e[f].totalCount;var m=new Array(v),w=0;for(f=0;f<n;f++)for(u=0;u<e.length;u++)f<i[u].length&&(m[w++]=i[u][f]);for(f=0;f<o;f++)for(u=0;u<e.length;u++)f<a[u].length&&(m[w++]=a[u][f]);return m};for(var i={MODE_NUMBER:1,MODE_ALPHA_NUM:2,MODE_8BIT_BYTE:4,MODE_KANJI:8},s={PATTERN_POSITION_TABLE:[[],[6,18],[6,22],[6,26],[6,30],[6,34],[6,22,38],[6,24,42],[6,26,46],[6,28,50],[6,30,54],[6,32,58],[6,34,62],[6,26,46,66],[6,26,48,70],[6,26,50,74],[6,30,54,78],[6,30,56,82],[6,30,58,86],[6,34,62,90],[6,28,50,72,94],[6,26,50,74,98],[6,30,54,78,102],[6,28,54,80,106],[6,32,58,84,110],[6,30,58,86,114],[6,34,62,90,118],[6,26,50,74,98,122],[6,30,54,78,102,126],[6,26,52,78,104,130],[6,30,56,82,108,134],[6,34,60,86,112,138],[6,30,58,86,114,142],[6,34,62,90,118,146],[6,30,54,78,102,126,150],[6,24,50,76,102,128,154],[6,28,54,80,106,132,158],[6,32,58,84,110,136,162],[6,26,54,82,110,138,166],[6,30,58,86,114,142,170]],G15:1335,G18:7973,G15_MASK:21522,getBCHTypeInfo:function(t){for(var e=t<<10;s.getBCHDigit(e)-s.getBCHDigit(s.G15)>=0;)e^=s.G15<<s.getBCHDigit(e)-s.getBCHDigit(s.G15);return(t<<10|e)^s.G15_MASK},getBCHTypeNumber:function(t){for(var e=t<<12;s.getBCHDigit(e)-s.getBCHDigit(s.G18)>=0;)e^=s.G18<<s.getBCHDigit(e)-s.getBCHDigit(s.G18);return t<<12|e},getBCHDigit:function(t){for(var e=0;0!=t;)e++,t>>>=1;return e},getPatternPosition:function(t){return s.PATTERN_POSITION_TABLE[t-1]},getMask:function(t,e,r){switch(t){case 0:return(e+r)%2==0;case 1:return e%2==0;case 2:return r%3==0;case 3:return(e+r)%3==0;case 4:return(Math.floor(e/2)+Math.floor(r/3))%2==0;case 5:return e*r%2+e*r%3==0;case 6:return(e*r%2+e*r%3)%2==0;case 7:return(e*r%3+(e+r)%2)%2==0;default:throw new Error("bad maskPattern:"+t)}},getErrorCorrectPolynomial:function(t){for(var e=new h([1],0),r=0;r<t;r++)e=e.multiply(new h([1,a.gexp(r)],0));return e},getLengthInBits:function(t,e){if(1<=e&&e<10)switch(t){case i.MODE_NUMBER:return 10;case i.MODE_ALPHA_NUM:return 9;case i.MODE_8BIT_BYTE:case i.MODE_KANJI:return 8;default:throw new Error("mode:"+t)}else if(e<27)switch(t){case i.MODE_NUMBER:return 12;case i.MODE_ALPHA_NUM:return 11;case i.MODE_8BIT_BYTE:return 16;case i.MODE_KANJI:return 10;default:throw new Error("mode:"+t)}else{if(!(e<41))throw new Error("type:"+e);switch(t){case i.MODE_NUMBER:return 14;case i.MODE_ALPHA_NUM:return 13;case i.MODE_8BIT_BYTE:return 16;case i.MODE_KANJI:return 12;default:throw new Error("mode:"+t)}}},getLostPoint:function(t){for(var e=t.getModuleCount(),r=0,n=0;n<e;n++)for(var o=0;o<e;o++){for(var i=0,s=t.isDark(n,o),a=-1;a<=1;a++)if(!(n+a<0||e<=n+a))for(var u=-1;u<=1;u++)o+u<0||e<=o+u||0==a&&0==u||s==t.isDark(n+a,o+u)&&i++;i>5&&(r+=3+i-5)}for(n=0;n<e-1;n++)for(o=0;o<e-1;o++){var h=0;t.isDark(n,o)&&h++,t.isDark(n+1,o)&&h++,t.isDark(n,o+1)&&h++,t.isDark(n+1,o+1)&&h++,0!=h&&4!=h||(r+=3)}for(n=0;n<e;n++)for(o=0;o<e-6;o++)t.isDark(n,o)&&!t.isDark(n,o+1)&&t.isDark(n,o+2)&&t.isDark(n,o+3)&&t.isDark(n,o+4)&&!t.isDark(n,o+5)&&t.isDark(n,o+6)&&(r+=40);for(o=0;o<e;o++)for(n=0;n<e-6;n++)t.isDark(n,o)&&!t.isDark(n+1,o)&&t.isDark(n+2,o)&&t.isDark(n+3,o)&&t.isDark(n+4,o)&&!t.isDark(n+5,o)&&t.isDark(n+6,o)&&(r+=40);var l=0;for(o=0;o<e;o++)for(n=0;n<e;n++)t.isDark(n,o)&&l++;return r+Math.abs(100*l/e/e-50)/5*10}},a={glog:function(t){if(t<1)throw new Error("glog("+t+")");return a.LOG_TABLE[t]},gexp:function(t){for(;t<0;)t+=255;for(;t>=256;)t-=255;return a.EXP_TABLE[t]},EXP_TABLE:new Array(256),LOG_TABLE:new Array(256)},u=0;u<8;u++)a.EXP_TABLE[u]=1<<u;for(u=8;u<256;u++)a.EXP_TABLE[u]=a.EXP_TABLE[u-4]^a.EXP_TABLE[u-5]^a.EXP_TABLE[u-6]^a.EXP_TABLE[u-8];for(u=0;u<255;u++)a.LOG_TABLE[a.EXP_TABLE[u]]=u;function h(t,e){if(null==t.length)throw new Error(t.length+"/"+e);for(var r=0;r<t.length&&0==t[r];)r++;this.num=new Array(t.length-r+e);for(var n=0;n<t.length-r;n++)this.num[n]=t[n+r]}function l(t,e){this.totalCount=t,this.dataCount=e}function d(){this.buffer=[],this.length=0}h.prototype={get:function(t){return this.num[t]},getLength:function(){return this.num.length},multiply:function(t){for(var e=new Array(this.getLength()+t.getLength()-1),r=0;r<this.getLength();r++)for(var n=0;n<t.getLength();n++)e[r+n]^=a.gexp(a.glog(this.get(r))+a.glog(t.get(n)));return new h(e,0)},mod:function(t){if(this.getLength()-t.getLength()<0)return this;for(var e=a.glog(this.get(0))-a.glog(t.get(0)),r=new Array(this.getLength()),n=0;n<this.getLength();n++)r[n]=this.get(n);for(n=0;n<t.getLength();n++)r[n]^=a.gexp(a.glog(t.get(n))+e);return new h(r,0).mod(t)}},l.RS_BLOCK_TABLE=[[1,26,19],[1,26,16],[1,26,13],[1,26,9],[1,44,34],[1,44,28],[1,44,22],[1,44,16],[1,70,55],[1,70,44],[2,35,17],[2,35,13],[1,100,80],[2,50,32],[2,50,24],[4,25,9],[1,134,108],[2,67,43],[2,33,15,2,34,16],[2,33,11,2,34,12],[2,86,68],[4,43,27],[4,43,19],[4,43,15],[2,98,78],[4,49,31],[2,32,14,4,33,15],[4,39,13,1,40,14],[2,121,97],[2,60,38,2,61,39],[4,40,18,2,41,19],[4,40,14,2,41,15],[2,146,116],[3,58,36,2,59,37],[4,36,16,4,37,17],[4,36,12,4,37,13],[2,86,68,2,87,69],[4,69,43,1,70,44],[6,43,19,2,44,20],[6,43,15,2,44,16],[4,101,81],[1,80,50,4,81,51],[4,50,22,4,51,23],[3,36,12,8,37,13],[2,116,92,2,117,93],[6,58,36,2,59,37],[4,46,20,6,47,21],[7,42,14,4,43,15],[4,133,107],[8,59,37,1,60,38],[8,44,20,4,45,21],[12,33,11,4,34,12],[3,145,115,1,146,116],[4,64,40,5,65,41],[11,36,16,5,37,17],[11,36,12,5,37,13],[5,109,87,1,110,88],[5,65,41,5,66,42],[5,54,24,7,55,25],[11,36,12],[5,122,98,1,123,99],[7,73,45,3,74,46],[15,43,19,2,44,20],[3,45,15,13,46,16],[1,135,107,5,136,108],[10,74,46,1,75,47],[1,50,22,15,51,23],[2,42,14,17,43,15],[5,150,120,1,151,121],[9,69,43,4,70,44],[17,50,22,1,51,23],[2,42,14,19,43,15],[3,141,113,4,142,114],[3,70,44,11,71,45],[17,47,21,4,48,22],[9,39,13,16,40,14],[3,135,107,5,136,108],[3,67,41,13,68,42],[15,54,24,5,55,25],[15,43,15,10,44,16],[4,144,116,4,145,117],[17,68,42],[17,50,22,6,51,23],[19,46,16,6,47,17],[2,139,111,7,140,112],[17,74,46],[7,54,24,16,55,25],[34,37,13],[4,151,121,5,152,122],[4,75,47,14,76,48],[11,54,24,14,55,25],[16,45,15,14,46,16],[6,147,117,4,148,118],[6,73,45,14,74,46],[11,54,24,16,55,25],[30,46,16,2,47,17],[8,132,106,4,133,107],[8,75,47,13,76,48],[7,54,24,22,55,25],[22,45,15,13,46,16],[10,142,114,2,143,115],[19,74,46,4,75,47],[28,50,22,6,51,23],[33,46,16,4,47,17],[8,152,122,4,153,123],[22,73,45,3,74,46],[8,53,23,26,54,24],[12,45,15,28,46,16],[3,147,117,10,148,118],[3,73,45,23,74,46],[4,54,24,31,55,25],[11,45,15,31,46,16],[7,146,116,7,147,117],[21,73,45,7,74,46],[1,53,23,37,54,24],[19,45,15,26,46,16],[5,145,115,10,146,116],[19,75,47,10,76,48],[15,54,24,25,55,25],[23,45,15,25,46,16],[13,145,115,3,146,116],[2,74,46,29,75,47],[42,54,24,1,55,25],[23,45,15,28,46,16],[17,145,115],[10,74,46,23,75,47],[10,54,24,35,55,25],[19,45,15,35,46,16],[17,145,115,1,146,116],[14,74,46,21,75,47],[29,54,24,19,55,25],[11,45,15,46,46,16],[13,145,115,6,146,116],[14,74,46,23,75,47],[44,54,24,7,55,25],[59,46,16,1,47,17],[12,151,121,7,152,122],[12,75,47,26,76,48],[39,54,24,14,55,25],[22,45,15,41,46,16],[6,151,121,14,152,122],[6,75,47,34,76,48],[46,54,24,10,55,25],[2,45,15,64,46,16],[17,152,122,4,153,123],[29,74,46,14,75,47],[49,54,24,10,55,25],[24,45,15,46,46,16],[4,152,122,18,153,123],[13,74,46,32,75,47],[48,54,24,14,55,25],[42,45,15,32,46,16],[20,147,117,4,148,118],[40,75,47,7,76,48],[43,54,24,22,55,25],[10,45,15,67,46,16],[19,148,118,6,149,119],[18,75,47,31,76,48],[34,54,24,34,55,25],[20,45,15,61,46,16]],l.getRSBlocks=function(t,e){var r=l.getRsBlockTable(t,e);if(null==r)throw new Error("bad rs block @ typeNumber:"+t+"/errorCorrectLevel:"+e);for(var n=r.length/3,o=[],i=0;i<n;i++)for(var s=r[3*i+0],a=r[3*i+1],u=r[3*i+2],h=0;h<s;h++)o.push(new l(a,u));return o},l.getRsBlockTable=function(t,e){switch(e){case 1:return l.RS_BLOCK_TABLE[4*(t-1)+0];case 0:return l.RS_BLOCK_TABLE[4*(t-1)+1];case 3:return l.RS_BLOCK_TABLE[4*(t-1)+2];case 2:return l.RS_BLOCK_TABLE[4*(t-1)+3];default:return}},d.prototype={get:function(t){var e=Math.floor(t/8);return 1==(this.buffer[e]>>>7-t%8&1)},put:function(t,e){for(var r=0;r<e;r++)this.putBit(1==(t>>>e-r-1&1))},getLengthInBits:function(){return this.length},putBit:function(t){var e=Math.floor(this.length/8);this.buffer.length<=e&&this.buffer.push(0),t&&(this.buffer[e]|=128>>>this.length%8),this.length++}};var f=[[17,14,11,7],[32,26,20,14],[53,42,32,24],[78,62,46,34],[106,84,60,44],[134,106,74,58],[154,122,86,64],[192,152,108,84],[230,180,130,98],[271,213,151,119],[321,251,177,137],[367,287,203,155],[425,331,241,177],[458,362,258,194],[520,412,292,220],[586,450,322,250],[644,504,364,280],[718,560,394,310],[792,624,442,338],[858,666,482,382],[929,711,509,403],[1003,779,565,439],[1091,857,611,461],[1171,911,661,511],[1273,997,715,535],[1367,1059,751,593],[1465,1125,805,625],[1528,1190,868,658],[1628,1264,908,698],[1732,1370,982,742],[1840,1452,1030,790],[1952,1538,1112,842],[2068,1628,1168,898],[2188,1722,1228,958],[2303,1809,1283,983],[2431,1911,1351,1051],[2563,1989,1423,1093],[2699,2099,1499,1139],[2809,2213,1579,1219],[2953,2331,1663,1273]];function g(t){if(this.options={padding:4,width:256,height:256,typeNumber:4,color:"#000000",background:"#ffffff",ecl:"M"},"string"==typeof t&&(t={content:t}),t)for(var e in t)this.options[e]=t[e];if("string"!=typeof this.options.content)throw new Error("Expected 'content' as string!");if(0===this.options.content.length)throw new Error("Expected 'content' to be non-empty!");if(!(this.options.padding>=0))throw new Error("Expected 'padding' value to be non-negative!");if(!(this.options.width>0&&this.options.height>0))throw new Error("Expected 'width' or 'height' value to be higher than zero!");var r=this.options.content,n=function(t,e){for(var r=function(t){var e=encodeURI(t).toString().replace(/\%[0-9a-fA-F]{2}/g,"a");return e.length+(e.length!=t?3:0)}(t),n=1,o=0,i=0,s=f.length;i<=s;i++){var a=f[i];if(!a)throw new Error("Content too long: expected "+o+" but got "+r);switch(e){case"L":o=a[0];break;case"M":o=a[1];break;case"Q":o=a[2];break;case"H":o=a[3];break;default:throw new Error("Unknwon error correction level: "+e)}if(r<=o)break;n++}if(n>f.length)throw new Error("Content too long");return n}(r,this.options.ecl),i=function(t){switch(t){case"L":return 1;case"M":return 0;case"Q":return 3;case"H":return 2;default:throw new Error("Unknwon error correction level: "+t)}}(this.options.ecl);this.qrcode=new o(n,i),this.qrcode.addData(r),this.qrcode.make()}g.prototype.svg=function(t){var e=this.options||{},r=this.qrcode.modules;void 0===t&&(t={container:e.container||"svg"});for(var n=void 0===e.pretty||!!e.pretty,o=n?"  ":"",i=n?"\r\n":"",s=e.width,a=e.height,u=r.length,h=s/(u+2*e.padding),l=a/(u+2*e.padding),d=void 0!==e.join&&!!e.join,f=void 0!==e.swap&&!!e.swap,g=void 0===e.xmlDeclaration||!!e.xmlDeclaration,c=void 0!==e.predefined&&!!e.predefined,p=c?o+'<defs><path id="qrmodule" d="M0 0 h'+l+" v"+h+' H0 z" style="fill:'+e.color+';shape-rendering:crispEdges;" /></defs>'+i:"",v=o+'<rect x="0" y="0" width="'+s+'" height="'+a+'" style="fill:'+e.background+';shape-rendering:crispEdges;"/>'+i,m="",w="",b=0;b<u;b++)for(var y=0;y<u;y++)if(r[y][b]){var E=y*h+e.padding*h,B=b*l+e.padding*l;if(f){var D=E;E=B,B=D}if(d){var L=h+E,C=l+B;E=Number.isInteger(E)?Number(E):E.toFixed(2),B=Number.isInteger(B)?Number(B):B.toFixed(2),L=Number.isInteger(L)?Number(L):L.toFixed(2),w+="M"+E+","+B+" V"+(C=Number.isInteger(C)?Number(C):C.toFixed(2))+" H"+L+" V"+B+" H"+E+" Z "}else m+=c?o+'<use x="'+E.toString()+'" y="'+B.toString()+'" href="#qrmodule" />'+i:o+'<rect x="'+E.toString()+'" y="'+B.toString()+'" width="'+h+'" height="'+l+'" style="fill:'+e.color+';shape-rendering:crispEdges;"/>'+i}d&&(m=o+'<path x="0" y="0" style="fill:'+e.color+';shape-rendering:crispEdges;" d="'+w+'" />');var _="";switch(t.container){case"svg":g&&(_+='<?xml version="1.0" standalone="yes"?>'+i),_+='<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="'+s+'" height="'+a+'">'+i,_+=p+v+m,_+="</svg>";break;case"svg-viewbox":g&&(_+='<?xml version="1.0" standalone="yes"?>'+i),_+='<svg xmlns="http://www.w3.org/2000/svg" version="1.1" viewBox="0 0 '+s+" "+a+'">'+i,_+=p+v+m,_+="</svg>";break;case"g":_+='<g width="'+s+'" height="'+a+'">'+i,_+=p+v+m,_+="</g>";break;default:_+=(p+v+m).replace(/^\s+/,"")}return _},g.prototype.save=function(t,e){var n=this.svg();"function"!=typeof e&&(e=function(t,e){});try{r(951).writeFile(t,n,e)}catch(t){e(t)}},t.exports=g},568:(t,e,r)=>{"use strict";Object.defineProperty(e,"__esModule",{value:!0});const n=r(805);e.default=t=>n.default({tablet:!0,featureDetect:!0,ua:t})},928:(t,e,r)=>{"use strict";Object.defineProperty(e,"__esModule",{value:!0});const n=r(304);e.default=t=>new n({content:t,join:!0}).svg()},562:(t,e,r)=>{"use strict";Object.defineProperty(e,"__esModule",{value:!0});const n=r(928);e.default=t=>{var e;const r=(null==t?void 0:t.header)||"Almost there...",o=(null==t?void 0:t.instructions)||"Use your phone's camera to scan the QR code below",i=(null==t?void 0:t.footer)||"or visit",s=(null==t?void 0:t.url)||(null===(e=null===window||void 0===window?void 0:window.location)||void 0===e?void 0:e.href)||"0.0.0.0",a=document.createElement("div");a.classList.add("zappar-mobile-only"),a.innerHTML=`\n    <style>\n        .zappar-mobile-only {\n            position: fixed;\n            width: 100%;\n            height: 100%;\n            top: 0px;\n            left: 0px;\n            z-index: 10000;\n            font-family: sans-serif;\n            color: white;\n            display: flex;\n            flex-direction: column;\n            align-items: center;\n            justify-content: center;\n        }\n        .zappar-inner {\n            max-width: 400px;\n            text-align: center;\n        }\n        .zappar-title {\n            font-size: 20px;\n        }\n        .zappar-text {\n            font-size: 14px;\n            padding: 15px;\n        }\n\n    </style>\n    <div class="zappar-inner">\n        <div class="zappar-title">${r}</div>\n        <div class="zappar-text">${o}</div>\n        <div class="zappar-svg-container"></div>\n        <div class="zappar-text">${i} <br/> ${s}</div>\n    </div>\n`,document.body.append(a),document.getElementsByClassName("zappar-svg-container")[0].innerHTML=n.default(s)}},951:()=>{}},e={};function r(n){var o=e[n];if(void 0!==o)return o.exports;var i=e[n]={exports:{}};return t[n](i,i.exports,r),i.exports}var n={};return(()=>{"use strict";var t=n;Object.defineProperty(t,"__esModule",{value:!0}),t.isMobile=t.showUI=void 0;var e=r(562);Object.defineProperty(t,"showUI",{enumerable:!0,get:function(){return e.default}});var o=r(568);Object.defineProperty(t,"isMobile",{enumerable:!0,get:function(){return o.default}})})(),n})()}));