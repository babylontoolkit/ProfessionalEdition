/*! For license information please see typed-array-buffer-schema.js.LICENSE.txt */
var Schema;(()=>{var t={6057:(t,e)=>{"use strict";function r(t){return"[object Object]"===Object.prototype.toString.call(t)}Object.defineProperty(e,"__esModule",{value:!0}),e.isPlainObject=function(t){var e,i;return!1!==r(t)&&(void 0===(e=t.constructor)||!1!==r(i=e.prototype)&&!1!==i.hasOwnProperty("isPrototypeOf"))}},1989:(t,e,r)=>{var i=r(1789),n=r(401),o=r(7667),s=r(1327),a=r(1866);function l(t){var e=-1,r=null==t?0:t.length;for(this.clear();++e<r;){var i=t[e];this.set(i[0],i[1])}}l.prototype.clear=i,l.prototype.delete=n,l.prototype.get=o,l.prototype.has=s,l.prototype.set=a,t.exports=l},8407:(t,e,r)=>{var i=r(7040),n=r(4125),o=r(2117),s=r(7518),a=r(4705);function l(t){var e=-1,r=null==t?0:t.length;for(this.clear();++e<r;){var i=t[e];this.set(i[0],i[1])}}l.prototype.clear=i,l.prototype.delete=n,l.prototype.get=o,l.prototype.has=s,l.prototype.set=a,t.exports=l},7071:(t,e,r)=>{var i=r(852)(r(5639),"Map");t.exports=i},3369:(t,e,r)=>{var i=r(4785),n=r(1285),o=r(6e3),s=r(9916),a=r(5265);function l(t){var e=-1,r=null==t?0:t.length;for(this.clear();++e<r;){var i=t[e];this.set(i[0],i[1])}}l.prototype.clear=i,l.prototype.delete=n,l.prototype.get=o,l.prototype.has=s,l.prototype.set=a,t.exports=l},2705:(t,e,r)=>{var i=r(5639).Symbol;t.exports=i},9932:t=>{t.exports=function(t,e){for(var r=-1,i=null==t?0:t.length,n=Array(i);++r<i;)n[r]=e(t[r],r,t);return n}},4865:(t,e,r)=>{var i=r(9465),n=r(7813),o=Object.prototype.hasOwnProperty;t.exports=function(t,e,r){var s=t[e];o.call(t,e)&&n(s,r)&&(void 0!==r||e in t)||i(t,e,r)}},8470:(t,e,r)=>{var i=r(7813);t.exports=function(t,e){for(var r=t.length;r--;)if(i(t[r][0],e))return r;return-1}},9465:(t,e,r)=>{var i=r(8777);t.exports=function(t,e,r){"__proto__"==e&&i?i(t,e,{configurable:!0,enumerable:!0,value:r,writable:!0}):t[e]=r}},4239:(t,e,r)=>{var i=r(2705),n=r(9607),o=r(2333),s=i?i.toStringTag:void 0;t.exports=function(t){return null==t?void 0===t?"[object Undefined]":"[object Null]":s&&s in Object(t)?n(t):o(t)}},8458:(t,e,r)=>{var i=r(3560),n=r(5346),o=r(3218),s=r(346),a=/^\[object .+?Constructor\]$/,l=Function.prototype,u=Object.prototype,c=l.toString,p=u.hasOwnProperty,y=RegExp("^"+c.call(p).replace(/[\\^$.*+?()[\]{}|]/g,"\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g,"$1.*?")+"$");t.exports=function(t){return!(!o(t)||n(t))&&(i(t)?y:a).test(s(t))}},611:(t,e,r)=>{var i=r(4865),n=r(1811),o=r(5776),s=r(3218),a=r(327);t.exports=function(t,e,r,l){if(!s(t))return t;for(var u=-1,c=(e=n(e,t)).length,p=c-1,y=t;null!=y&&++u<c;){var h=a(e[u]),_=r;if("__proto__"===h||"constructor"===h||"prototype"===h)return t;if(u!=p){var f=y[h];void 0===(_=l?l(f,h,y):void 0)&&(_=s(f)?f:o(e[u+1])?[]:{})}i(y,h,_),y=y[h]}return t}},531:(t,e,r)=>{var i=r(2705),n=r(9932),o=r(1469),s=r(3448),a=i?i.prototype:void 0,l=a?a.toString:void 0;t.exports=function t(e){if("string"==typeof e)return e;if(o(e))return n(e,t)+"";if(s(e))return l?l.call(e):"";var r=e+"";return"0"==r&&1/e==-1/0?"-0":r}},1811:(t,e,r)=>{var i=r(1469),n=r(5403),o=r(5514),s=r(9833);t.exports=function(t,e){return i(t)?t:n(t,e)?[t]:o(s(t))}},4429:(t,e,r)=>{var i=r(5639)["__core-js_shared__"];t.exports=i},8777:(t,e,r)=>{var i=r(852),n=function(){try{var t=i(Object,"defineProperty");return t({},"",{}),t}catch(t){}}();t.exports=n},1957:(t,e,r)=>{var i="object"==typeof r.g&&r.g&&r.g.Object===Object&&r.g;t.exports=i},5050:(t,e,r)=>{var i=r(7019);t.exports=function(t,e){var r=t.__data__;return i(e)?r["string"==typeof e?"string":"hash"]:r.map}},852:(t,e,r)=>{var i=r(8458),n=r(7801);t.exports=function(t,e){var r=n(t,e);return i(r)?r:void 0}},9607:(t,e,r)=>{var i=r(2705),n=Object.prototype,o=n.hasOwnProperty,s=n.toString,a=i?i.toStringTag:void 0;t.exports=function(t){var e=o.call(t,a),r=t[a];try{t[a]=void 0;var i=!0}catch(t){}var n=s.call(t);return i&&(e?t[a]=r:delete t[a]),n}},7801:t=>{t.exports=function(t,e){return null==t?void 0:t[e]}},1789:(t,e,r)=>{var i=r(4536);t.exports=function(){this.__data__=i?i(null):{},this.size=0}},401:t=>{t.exports=function(t){var e=this.has(t)&&delete this.__data__[t];return this.size-=e?1:0,e}},7667:(t,e,r)=>{var i=r(4536),n=Object.prototype.hasOwnProperty;t.exports=function(t){var e=this.__data__;if(i){var r=e[t];return"__lodash_hash_undefined__"===r?void 0:r}return n.call(e,t)?e[t]:void 0}},1327:(t,e,r)=>{var i=r(4536),n=Object.prototype.hasOwnProperty;t.exports=function(t){var e=this.__data__;return i?void 0!==e[t]:n.call(e,t)}},1866:(t,e,r)=>{var i=r(4536);t.exports=function(t,e){var r=this.__data__;return this.size+=this.has(t)?0:1,r[t]=i&&void 0===e?"__lodash_hash_undefined__":e,this}},5776:t=>{var e=/^(?:0|[1-9]\d*)$/;t.exports=function(t,r){var i=typeof t;return!!(r=null==r?9007199254740991:r)&&("number"==i||"symbol"!=i&&e.test(t))&&t>-1&&t%1==0&&t<r}},5403:(t,e,r)=>{var i=r(1469),n=r(3448),o=/\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/,s=/^\w*$/;t.exports=function(t,e){if(i(t))return!1;var r=typeof t;return!("number"!=r&&"symbol"!=r&&"boolean"!=r&&null!=t&&!n(t))||s.test(t)||!o.test(t)||null!=e&&t in Object(e)}},7019:t=>{t.exports=function(t){var e=typeof t;return"string"==e||"number"==e||"symbol"==e||"boolean"==e?"__proto__"!==t:null===t}},5346:(t,e,r)=>{var i,n=r(4429),o=(i=/[^.]+$/.exec(n&&n.keys&&n.keys.IE_PROTO||""))?"Symbol(src)_1."+i:"";t.exports=function(t){return!!o&&o in t}},7040:t=>{t.exports=function(){this.__data__=[],this.size=0}},4125:(t,e,r)=>{var i=r(8470),n=Array.prototype.splice;t.exports=function(t){var e=this.__data__,r=i(e,t);return!(r<0||(r==e.length-1?e.pop():n.call(e,r,1),--this.size,0))}},2117:(t,e,r)=>{var i=r(8470);t.exports=function(t){var e=this.__data__,r=i(e,t);return r<0?void 0:e[r][1]}},7518:(t,e,r)=>{var i=r(8470);t.exports=function(t){return i(this.__data__,t)>-1}},4705:(t,e,r)=>{var i=r(8470);t.exports=function(t,e){var r=this.__data__,n=i(r,t);return n<0?(++this.size,r.push([t,e])):r[n][1]=e,this}},4785:(t,e,r)=>{var i=r(1989),n=r(8407),o=r(7071);t.exports=function(){this.size=0,this.__data__={hash:new i,map:new(o||n),string:new i}}},1285:(t,e,r)=>{var i=r(5050);t.exports=function(t){var e=i(this,t).delete(t);return this.size-=e?1:0,e}},6e3:(t,e,r)=>{var i=r(5050);t.exports=function(t){return i(this,t).get(t)}},9916:(t,e,r)=>{var i=r(5050);t.exports=function(t){return i(this,t).has(t)}},5265:(t,e,r)=>{var i=r(5050);t.exports=function(t,e){var r=i(this,t),n=r.size;return r.set(t,e),this.size+=r.size==n?0:1,this}},4523:(t,e,r)=>{var i=r(8306);t.exports=function(t){var e=i(t,(function(t){return 500===r.size&&r.clear(),t})),r=e.cache;return e}},4536:(t,e,r)=>{var i=r(852)(Object,"create");t.exports=i},2333:t=>{var e=Object.prototype.toString;t.exports=function(t){return e.call(t)}},5639:(t,e,r)=>{var i=r(1957),n="object"==typeof self&&self&&self.Object===Object&&self,o=i||n||Function("return this")();t.exports=o},5514:(t,e,r)=>{var i=r(4523),n=/[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g,o=/\\(\\)?/g,s=i((function(t){var e=[];return 46===t.charCodeAt(0)&&e.push(""),t.replace(n,(function(t,r,i,n){e.push(i?n.replace(o,"$1"):r||t)})),e}));t.exports=s},327:(t,e,r)=>{var i=r(3448);t.exports=function(t){if("string"==typeof t||i(t))return t;var e=t+"";return"0"==e&&1/t==-1/0?"-0":e}},346:t=>{var e=Function.prototype.toString;t.exports=function(t){if(null!=t){try{return e.call(t)}catch(t){}try{return t+""}catch(t){}}return""}},7813:t=>{t.exports=function(t,e){return t===e||t!=t&&e!=e}},1469:t=>{var e=Array.isArray;t.exports=e},3560:(t,e,r)=>{var i=r(4239),n=r(3218);t.exports=function(t){if(!n(t))return!1;var e=i(t);return"[object Function]"==e||"[object GeneratorFunction]"==e||"[object AsyncFunction]"==e||"[object Proxy]"==e}},3218:t=>{t.exports=function(t){var e=typeof t;return null!=t&&("object"==e||"function"==e)}},7005:t=>{t.exports=function(t){return null!=t&&"object"==typeof t}},3448:(t,e,r)=>{var i=r(4239),n=r(7005);t.exports=function(t){return"symbol"==typeof t||n(t)&&"[object Symbol]"==i(t)}},8306:(t,e,r)=>{var i=r(3369);function n(t,e){if("function"!=typeof t||null!=e&&"function"!=typeof e)throw new TypeError("Expected a function");var r=function(){var i=arguments,n=e?e.apply(this,i):i[0],o=r.cache;if(o.has(n))return o.get(n);var s=t.apply(this,i);return r.cache=o.set(n,s)||o,s};return r.cache=new(n.Cache||i),r}n.Cache=i,t.exports=n},6968:(t,e,r)=>{var i=r(611);t.exports=function(t,e,r){return null==t?t:i(t,e,r)}},9833:(t,e,r)=>{var i=r(531);t.exports=function(t){return null==t?"":i(t)}},6818:(t,e,r)=>{"use strict";Object.defineProperty(e,"__esModule",{value:!0}),e.deepSortObject=void 0;const i=r(6057),n=t=>{return!((0,i.isPlainObject)(t)||(e=t,Array.isArray(e)&&(!(e.length>0)||"object"==typeof e[0])));var e},o=([t,e],[r,i])=>n(e)&&n(i)?t.localeCompare(r):n(e)?-1:n(i)?1:t.localeCompare(r),s=(t,e)=>{let r;return Array.isArray(t)?t.map((function(t){return s(t,e)})):(0,i.isPlainObject)(t)?(r={},Object.entries(t).sort(e||o).forEach((function([t,i]){r[t]=s(i,e)})),r):t};e.deepSortObject=s},5563:(t,e,r)=>{"use strict";Object.defineProperty(e,"__esModule",{value:!0}),e.Lib=void 0;const i=r(6818),n=r(1734);class o{static newHash(t,e){let r=(t=>{let e=0;for(let r=0;r<t.length;r++)e=(e<<5)-e+t.charCodeAt(r),e|=0;return e*=254785,Math.abs(e).toString(32).slice(2,6)})(JSON.stringify(e)+t);if(4!==r.length)throw new Error("Hash has not length of 4");return`#${r}`}static schema(t,e){e=(0,i.deepSortObject)(e);const r=o.newHash(t,e),s=new n.Schema(r,t,e);return this._schemas.set(r,s),s}}e.Lib=o,o._schemas=new Map,o.getIdFromBuffer=t=>{const e=new DataView(t);let r="";for(let t=0;t<5;t++){const i=e.getUint8(t);r+=String.fromCharCode(i)}return r},o.getIdFromSchema=t=>t.id,o.getIdFromModel=t=>t.schema.id},3134:(t,e,r)=>{"use strict";Object.defineProperty(e,"__esModule",{value:!0}),e.Model=void 0;const i=r(488);class n extends i.Serialize{constructor(t,e=8){super(t,e),this.schema=t}}e.Model=n},1734:(t,e)=>{"use strict";Object.defineProperty(e,"__esModule",{value:!0}),e.Schema=void 0;class r{constructor(t,e,i){this._id=t,this._name=e,this._struct=i,this._bytes=0,r.Validation(i),this.calcBytes()}static Validation(t){}get id(){return this._id}get name(){return this._name}isSpecialType(t){return!Object.keys(t).filter((t=>"type"!=t&&"digits"!=t&&"length"!=t)).length}calcBytes(){const t=e=>{var r,i;for(let n in e){const o=(null==e?void 0:e._type)||!!this.isSpecialType(e)&&(null===(r=null==e?void 0:e.type)||void 0===r?void 0:r._type),s=(null==e?void 0:e._bytes)||!!this.isSpecialType(e)&&(null===(i=null==e?void 0:e.type)||void 0===i?void 0:i._bytes);if(!o&&e.hasOwnProperty(n))"object"==typeof e[n]&&t(e[n]);else{if("_type"!==n&&"type"!==n)continue;if(!s)continue;if("String8"===o||"String16"===o){const t=e.length||12;this._bytes+=s*t}else this._bytes+=s}}};t(this._struct)}get struct(){return this._struct}get bytes(){return this._bytes}}e.Schema=r},488:function(t,e,r){"use strict";var i=this&&this.__importDefault||function(t){return t&&t.__esModule?t:{default:t}};Object.defineProperty(e,"__esModule",{value:!0}),e.Serialize=void 0;const n=r(5563),o=i(r(6968)),s=r(6818);e.Serialize=class{constructor(t,e){this.schema=t,this.bufferSize=e,this._buffer=new ArrayBuffer(0),this._dataView=new DataView(this._buffer),this._bytes=0}refresh(){this._buffer=new ArrayBuffer(1024*this.bufferSize),this._dataView=new DataView(this._buffer),this._bytes=0}cropString(t,e){return t.padEnd(e," ").slice(0,e)}isSpecialType(t){return!Object.keys(t).filter((t=>"type"!=t&&"digits"!=t&&"length"!=t)).length}boolArrayToInt(t){let e="1";for(var r=0;r<t.length;r++)e+=+!!t[r];return parseInt(e,2)}intToBoolArray(t){return[...(t>>>0).toString(2)].map((t=>"0"!=t)).slice(1)}flatten(t,e){let r=[];const i=(t,e)=>{var n,o,s,a,l,u,c,p,y,h,_,f;let d;for(d in(null==t?void 0:t._id)?r.push({d:t._id,t:"String8"}):(null===(n=null==t?void 0:t[0])||void 0===n?void 0:n._id)&&r.push({d:t[0]._id,t:"String8"}),(null==t?void 0:t._struct)?t=t._struct:(null===(o=null==t?void 0:t[0])||void 0===o?void 0:o._struct)&&(t=t[0]._struct),e)if(e.hasOwnProperty(d))if("object"==typeof e[d])Array.isArray(e)?i(t,e[parseInt(d)]):"BitArray8"===(null===(s=t[d])||void 0===s?void 0:s._type)||"BitArray16"===(null===(a=t[d])||void 0===a?void 0:a._type)?r.push({d:this.boolArrayToInt(e[d]),t:t[d]._type}):i(t[d],e[d]);else if((null===(u=null===(l=t[d])||void 0===l?void 0:l.type)||void 0===u?void 0:u._type)&&this.isSpecialType(t[d])){if((null===(c=t[d])||void 0===c?void 0:c.digits)&&(e[d]*=Math.pow(10,t[d].digits),e[d]=parseInt(e[d].toFixed(0))),null===(p=t[d])||void 0===p?void 0:p.length){const r=null===(y=t[d])||void 0===y?void 0:y.length;e[d]=this.cropString(e[d],r)}r.push({d:e[d],t:t[d].type._type})}else(null===(h=t[d])||void 0===h?void 0:h._type)&&("String8"!==(null===(_=t[d])||void 0===_?void 0:_._type)&&"String16"!==(null===(f=t[d])||void 0===f?void 0:f._type)||(e[d]=this.cropString(e[d],12)),r.push({d:e[d],t:t[d]._type}))};return i(t,e),r}toBuffer(t){let e=(0,s.deepSortObject)(t);const r=JSON.parse(JSON.stringify(e));this.refresh(),this.flatten(this.schema,r).forEach(((t,e)=>{if("String8"===t.t)for(let e=0;e<t.d.length;e++)this._dataView.setUint8(this._bytes,t.d[e].charCodeAt(0)),this._bytes++;else if("String16"===t.t)for(let e=0;e<t.d.length;e++)this._dataView.setUint16(this._bytes,t.d[e].charCodeAt(0)),this._bytes+=2;else"Int8Array"===t.t?(this._dataView.setInt8(this._bytes,t.d),this._bytes++):"Uint8Array"===t.t?(this._dataView.setUint8(this._bytes,t.d),this._bytes++):"Int16Array"===t.t?(this._dataView.setInt16(this._bytes,t.d),this._bytes+=2):"Uint16Array"===t.t?(this._dataView.setUint16(this._bytes,t.d),this._bytes+=2):"Int32Array"===t.t?(this._dataView.setInt32(this._bytes,t.d),this._bytes+=4):"Uint32Array"===t.t?(this._dataView.setUint32(this._bytes,t.d),this._bytes+=4):"BigInt64Array"===t.t?(this._dataView.setBigInt64(this._bytes,BigInt(t.d)),this._bytes+=8):"BigUint64Array"===t.t?(this._dataView.setBigUint64(this._bytes,BigInt(t.d)),this._bytes+=8):"Float32Array"===t.t?(this._dataView.setFloat32(this._bytes,t.d),this._bytes+=4):"Float64Array"===t.t?(this._dataView.setFloat64(this._bytes,t.d),this._bytes+=8):"BitArray8"===t.t?(this._dataView.setUint8(this._bytes,t.d),this._bytes++):"BitArray16"===t.t?(this._dataView.setUint16(this._bytes,t.d),this._bytes+=2):console.log("ERROR: Something unexpected happened!")}));const i=new ArrayBuffer(this._bytes),n=new DataView(i);for(let t=0;t<this._bytes;t++)n.setUint8(t,this._dataView.getUint8(t));return i}fromBuffer(t){let e=0,r=[];const i=new DataView(t),s=Array.from(new Int8Array(t));for(;e>-1;)e=s.indexOf(35,e),-1!==e&&(r.push(e),e++);let a=[];r.forEach((t=>{let e="";for(let r=0;r<5;r++)e+=String.fromCharCode(s[t+r]);a.push(e)}));let l=[];a.forEach(((t,e)=>{n.Lib._schemas.get(t)&&l.push({id:t,schema:n.Lib._schemas.get(t),startsAt:r[e]+5})}));let u={},c=0,p={};const y=t=>{var e,r;let n={};if("object"==typeof t)for(let o in t)if(t.hasOwnProperty(o)){const s=t[o];let a;if((null===(e=null==s?void 0:s.type)||void 0===e?void 0:e._type)&&(null===(r=null==s?void 0:s.type)||void 0===r?void 0:r._bytes)&&this.isSpecialType(s)&&(a=s,s._type=s.type._type,s._bytes=s.type._bytes),s&&s._type&&s._bytes){const t=s._type,e=s._bytes;let r;if("String8"===t){r="";const t=s.length||12;for(let e=0;e<t;e++)r+=String.fromCharCode(i.getUint8(c)),c++}if("String16"===t){r="";const t=s.length||12;for(let e=0;e<t;e++)r+=String.fromCharCode(i.getUint16(c)),c+=2}"Int8Array"===t&&(r=i.getInt8(c),c+=e),"Uint8Array"===t&&(r=i.getUint8(c),c+=e),"Int16Array"===t&&(r=i.getInt16(c),c+=e),"Uint16Array"===t&&(r=i.getUint16(c),c+=e),"Int32Array"===t&&(r=i.getInt32(c),c+=e),"Uint32Array"===t&&(r=i.getUint32(c),c+=e),"BigInt64Array"===t&&(r=parseInt(i.getBigInt64(c).toString()),c+=e),"BigUint64Array"===t&&(r=parseInt(i.getBigUint64(c).toString()),c+=e),"Float32Array"===t&&(r=i.getFloat32(c),c+=e),"Float64Array"===t&&(r=i.getFloat64(c),c+=e),"BitArray8"===t&&(r=this.intToBoolArray(i.getUint8(c)),c+=e),"BitArray16"===t&&(r=this.intToBoolArray(i.getUint16(c)),c+=e),"number"==typeof r&&(null==a?void 0:a.digits)&&(r*=Math.pow(10,-a.digits),r=parseFloat(r.toFixed(a.digits))),n={...n,[o]:r}}}return n};l.forEach(((e,r)=>{var i,n,o;let s=null===(i=e.schema)||void 0===i?void 0:i.struct,a=e.startsAt,u=t.byteLength,h=(null===(n=e.schema)||void 0===n?void 0:n.id)||"XX";"XX"===h&&console.error("ERROR: Something went horribly wrong!");try{u=l[r+1].startsAt-5}catch{}const _=(null===(o=e.schema)||void 0===o?void 0:o.bytes)||1,f=(u-a)/_;for(let t=0;t<f;t++){c=a+t*_;let e=y(s);f<=1?p[h]={...e}:(void 0===p[h]&&(p[h]=[]),p[h].push(e))}})),u={};const h=(t,e,r,i="",n=!1)=>{if(t&&t._id&&t._id===e){let t=i.replace(/_struct\./,"").replace(/\.$/,"");n&&!Array.isArray(r)&&(r=[r]),""===t?u={...u,...r}:(0,o.default)(u,t,r)}else for(const n in t)if(t.hasOwnProperty(n)&&"object"==typeof t[n]){let o=Array.isArray(t)?"":`${n}.`;h(t[n],e,r,i+o,Array.isArray(t))}};for(let t=0;t<Object.keys(p).length;t++){const e=Object.keys(p)[t],r=p[e];h(this.schema,e,r,"")}return u}}},7826:(t,e)=>{"use strict";Object.defineProperty(e,"__esModule",{value:!0}),e.bool16=e.bool8=e.string16=e.string8=e.float64=e.float32=e.uint64=e.int64=e.uint32=e.int32=e.uint16=e.int16=e.uint8=e.int8=void 0,e.int8={_type:"Int8Array",_bytes:1},e.uint8={_type:"Uint8Array",_bytes:1},e.int16={_type:"Int16Array",_bytes:2},e.uint16={_type:"Uint16Array",_bytes:2},e.int32={_type:"Int32Array",_bytes:4},e.uint32={_type:"Uint32Array",_bytes:4},e.int64={_type:"BigInt64Array",_bytes:8},e.uint64={_type:"BigUint64Array",_bytes:8},e.float32={_type:"Float32Array",_bytes:4},e.float64={_type:"Float64Array",_bytes:8},e.string8={_type:"String8",_bytes:1},e.string16={_type:"String16",_bytes:2},e.bool8={_type:"BitArray8",_bytes:1},e.bool16={_type:"BitArray16",_bytes:2}}},e={};function r(i){var n=e[i];if(void 0!==n)return n.exports;var o=e[i]={exports:{}};return t[i].call(o.exports,o,o.exports,r),o.exports}r.g=function(){if("object"==typeof globalThis)return globalThis;try{return this||new Function("return this")()}catch(t){if("object"==typeof window)return window}}();var i={};(()=>{"use strict";var t=i;const e=r(5563),n=r(3134),o=r(7826);t.default={BufferSchema:e.Lib,Model:n.Model,int8:o.int8,uint8:o.uint8,int16:o.int16,uint16:o.uint16,int32:o.int32,uint32:o.uint32,int64:o.int64,uint64:o.uint64,float32:o.float32,float64:o.float64,string8:o.string8,string16:o.string16}})(),Schema=i.default})();