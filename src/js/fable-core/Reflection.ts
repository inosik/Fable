import { compareArraysWith, equalArraysWith } from "./Util";

export type FieldInfo = [string, TypeInfo];

export class CaseInfo {
  constructor(public tag: number,
              public name: string,
              public fields?: TypeInfo[]) {
  }
  get isRepresentedAsString() {
    return this.fields == null;
  }
}

export class TypeInfo {
  public fields?: FieldInfo[];
  public cases?: CaseInfo[];
  constructor(public fullname: string,
              public generics?: TypeInfo[],
              options?: { fields?: FieldInfo[], cases?: CaseInfo[] }) {
    this.fields = options != null ? options.fields : void 0;
    this.cases = options != null ? options.cases : void 0;
  }
  public toString() {
    return this.fullname; // TODO: Print also generics?
  }
  public Equals(other: TypeInfo) {
    return equals(this, other);
  }
  public CompareTo(other: TypeInfo) {
    return compare(this, other);
  }
}

export function equals(t1: TypeInfo, t2: TypeInfo): boolean {
  return t1.fullname === t2.fullname
    && equalArraysWith(t1.generics, t2.generics, equals);
}

// System.Type is not comparable in .NET, but let's implement this
// in case users want to create a dictionary with types as keys
export function compare(t1: TypeInfo, t2: TypeInfo): number {
  if (t1.fullname !== t2.fullname) {
    return t1.fullname < t2.fullname ? -1 : 1;
  } else {
    return compareArraysWith(t1.generics, t2.generics, compare);
  }
}

export function type(fullname: string, generics?: TypeInfo[]): TypeInfo {
  return new TypeInfo(fullname, generics);
}

export function recursiveType(info: TypeInfo): TypeInfo {
  function checkRecursive(m: Map<string, TypeInfo>, info: TypeInfo) {
    // Check first if there're subtypes
    if (info.generics != null || info.fields != null || info.cases != null) {
      m.set(info.fullname, info);
      if (info.generics != null) {
        for (let i = 0; i < info.generics.length; i++) {
          const t = info.generics[i];
          if (m.has(t.fullname)) {
            info.generics[i] = m.get(t.fullname);
          } else {
            checkRecursive(m, t);
          }
        }
      }

      if (info.fields != null) {
        for (let i = 0; i < info.fields.length; i++) {
          const [name, t] = info.fields[i];
          if (m.has(t.fullname)) {
            info.fields[i] = [name, m.get(t.fullname)];
          } else {
            checkRecursive(m, t);
          }
        }
      }

      if (info.cases != null) {
        for (let i = 0; i < info.cases.length; i++) {
          const uci = info.cases[i];
          if (uci.fields != null) {
            for (let j = 0; j < uci.fields.length; j++) {
              const t = uci.fields[j];
              if (m.has(t.fullname)) {
                uci.fields[j] = m.get(t.fullname);
              } else {
                checkRecursive(m, t);
              }
            }
          }
        }
      }
    }
  }
  checkRecursive(new Map(), info);
  return info;
}

export function record(fullname: string, generics: TypeInfo[], ...fields: FieldInfo[]): TypeInfo {
  return new TypeInfo(fullname, generics, { fields });
}

export type CaseInfoInput = string | [string, TypeInfo[]];

export function union(fullname: string, generics: TypeInfo[], ...cases: CaseInfoInput[]): TypeInfo {
  // If the input is just a string, don't initialize `fields` so we know the case is represented as a string
  return new TypeInfo(fullname, generics, { cases: cases.map((x, i) =>
    typeof x === "string" ? new CaseInfo(i, x) : new CaseInfo(i, x[0], x[1])) });
}

export function tuple(...generics: TypeInfo[]): TypeInfo {
  return new TypeInfo("System.Tuple`" + generics.length, generics);
}

export function delegate(...generics: TypeInfo[]): TypeInfo {
  return new TypeInfo("System.Func`" + generics.length, generics);
}

export function lambda(argType: TypeInfo, returnType: TypeInfo): TypeInfo {
  return new TypeInfo("Microsoft.FSharp.Core.FSharpFunc`2", [argType, returnType]);
}

export function option(generic: TypeInfo): TypeInfo {
  return new TypeInfo("Microsoft.FSharp.Core.FSharpOption`1", [generic]);
}

export function list(generic: TypeInfo): TypeInfo {
  return new TypeInfo("Microsoft.FSharp.Collections.FSharpList`1", [generic]);
}

export function array(generic: TypeInfo): TypeInfo {
  return new TypeInfo(generic.fullname + "[]", [generic]);
}

export const obj: TypeInfo = new TypeInfo("System.Object");
export const unit: TypeInfo = new TypeInfo("Microsoft.FSharp.Core.Unit");
export const char: TypeInfo = new TypeInfo("System.Char");
export const string: TypeInfo = new TypeInfo("System.String");
export const bool: TypeInfo = new TypeInfo("System.Boolean");
export const int8: TypeInfo = new TypeInfo("System.SByte");
export const uint8: TypeInfo = new TypeInfo("System.Byte");
export const int16: TypeInfo = new TypeInfo("System.Int16");
export const uint16: TypeInfo = new TypeInfo("System.UInt16");
export const int32: TypeInfo = new TypeInfo("System.Int32");
export const uint32: TypeInfo = new TypeInfo("System.UInt32");
export const float32: TypeInfo = new TypeInfo("System.Single");
export const float64: TypeInfo = new TypeInfo("System.Double");
export const decimal: TypeInfo = new TypeInfo("System.Decimal");

export function name(info: FieldInfo | CaseInfo | TypeInfo): string {
  if (Array.isArray(info)) {
    return info[0];
  } else if (info instanceof CaseInfo) {
    return info.name;
  } else {
    const i = info.fullname.lastIndexOf(".");
    return i === -1 ? info.fullname : info.fullname.substr(i + 1);
  }
}

export function namespace(t: TypeInfo) {
  const i = t.fullname.lastIndexOf(".");
  return i === -1 ? "" : t.fullname.substr(0, i);
}

export function isArray(t: TypeInfo): boolean {
  return t.fullname.endsWith("[]");
}

export function getElementType(t: TypeInfo): TypeInfo {
  return isArray(t) ? t.generics[0] : null;
}

export function isGenericType(t: TypeInfo) {
  return t.generics != null && t.generics.length > 0;
}

/**
 * This doesn't replace types for fields (records) or cases (unions)
 * but it should be enough for type comparison purposes
 */
export function getGenericTypeDefinition(t: TypeInfo) {
  return t.generics == null ? t : new TypeInfo(t.fullname, t.generics.map(() => obj));
}

// FSharpType

export function getUnionCases(t: TypeInfo): CaseInfo[] {
  if (Array.isArray(t.cases)) {
    return t.cases;
  } else {
    throw new Error(`${t.fullname} is not an F# union type`);
  }
}

export function getRecordElements(t: TypeInfo): FieldInfo[] {
  if (Array.isArray(t.fields)) {
    return t.fields;
  } else {
    throw new Error(`${t.fullname} is not an F# record type`);
  }
}

export function getTupleElements(t: TypeInfo): TypeInfo[] {
  if (isTuple(t)) {
    return t.generics;
  } else {
    throw new Error(`${t.fullname} is not a tuple type`);
  }
}

export function getFunctionElements(t: TypeInfo): [TypeInfo, TypeInfo] {
  if (isFunction(t)) {
    return [t.generics[0], t.generics[1]];
  } else {
    throw new Error(`${t.fullname} is not an F# function type`);
  }
}

export function isUnion(t: TypeInfo): boolean {
  return Array.isArray(t.cases);
}

export function isRecord(t: TypeInfo): boolean {
  return Array.isArray(t.fields);
}

export function isTuple(t: TypeInfo): boolean {
  return t.fullname.startsWith("System.Tuple");
}

// In .NET this is false for delegates
export function isFunction(t: TypeInfo): boolean {
  return t.fullname === "Microsoft.FSharp.Core.FSharpFunc`2";
}

// FSharpValue

export function getUnionFields(v: any, t: TypeInfo): [CaseInfo, any[]] {
  const cases = getUnionCases(t);
  // Unions without fields may be represented in JS as a string
  const caseName: string = typeof v === "string" ? v : v[0];
  const case_ = cases.find((x) => x.name === caseName);
  if (case_ == null) {
    throw new Error(`Cannot find case ${caseName} in union type`);
  }
  return [case_, Array.isArray(v) ? v.slice(1) : []];
}

export function getUnionCaseFields(uci: CaseInfo): FieldInfo[] {
  return uci.fields == null ? [] : uci.fields.map((t, i) => ["Data" + i, t] as FieldInfo);
}

export function getRecordFields(v: any): any[] {
  return Object.keys(v).map((k) => v[k]);
}

export function getRecordField(v: any, field: FieldInfo): any {
  return v[field[0]];
}

export function getTupleFields(v: any): any[] {
  return v;
}

export function getTupleField(v: any, i: number): any {
  return v[i];
}

export function makeUnion(uci: CaseInfo, values: any[]): any {
  return uci.isRepresentedAsString ? uci.name : [uci.name].concat(values);
}

export function makeRecord(t: TypeInfo, values: any[]): any {
  const o: any = {};
  const fields = getRecordElements(t);
  if (fields.length !== values.length) {
    throw new Error(`Expected an array of length ${fields.length} but got ${values.length}`);
  }
  for (let i = 0; i < fields.length; i++) {
    o[fields[i][0]] = values[i];
  }
  return o;
}

export function makeTuple(values: any[], t: TypeInfo): any {
  return values;
}
