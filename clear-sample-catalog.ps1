param([switch]$Apply)
$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '../QLNPdasua10.11/QuanLyNghiPhep'
$native=Join-Path $root 'bin/Debug/net8.0/runtimes/win-x64/native/e_sqlite3.dll'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class CleanupSqlite {
 [DllImport(@"$native",CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path,out IntPtr db);
 [DllImport(@"$native",CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,Callback cb,IntPtr arg,out IntPtr error);
 [DllImport(@"$native",CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_close(IntPtr db);
 public delegate int Callback(IntPtr arg,int n,IntPtr values,IntPtr names);
 public static void Run(string path,string sql) {
 IntPtr db,error; if(sqlite3_open(path,out db)!=0) throw new Exception("Open failed");
 try { Callback cb=(a,n,v,k)=>{ for(int i=0;i<n;i++) Console.Write(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(k,i*IntPtr.Size))+"="+Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(v,i*IntPtr.Size))+" "); Console.WriteLine(); return 0;};
 if(sqlite3_exec(db,sql,cb,IntPtr.Zero,out error)!=0) throw new Exception(Marshal.PtrToStringUTF8(error));
 } finally {sqlite3_close(db);}
 }
}
"@
$business=Join-Path $root 'AppData/business.db'
$accounts=Join-Path $root 'AppData/accounts.db'
[CleanupSqlite]::Run($business,'SELECT name, sql FROM sqlite_master WHERE type=''table''; SELECT COUNT(*) AS Subjects FROM ClassSubjects; SELECT COUNT(*) AS LinkedRequests FROM Requests WHERE SubjectId IS NOT NULL;')
[CleanupSqlite]::Run($accounts,'SELECT name FROM sqlite_master WHERE type=''table''; SELECT Id,UserName,FacultyId,FacultyName FROM AspNetUsers WHERE FacultyId<>'''';')
if ($Apply) {
 $stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
 $backup=Join-Path $root "AppData/backups/clear-catalog-$stamp"
 New-Item -ItemType Directory -Path $backup | Out-Null
 foreach($item in @(@($business,'business.db'),@($accounts,'accounts.db'))) {
  $dest=(Join-Path $backup $item[1]).Replace("'","''")
  [CleanupSqlite]::Run($item[0],"VACUUM INTO '$dest';")
 }
 [CleanupSqlite]::Run($business,"PRAGMA foreign_keys=ON; ATTACH DATABASE '$($accounts.Replace("'","''"))' AS auth; BEGIN IMMEDIATE; UPDATE Requests SET SubjectId=NULL WHERE SubjectId IN (SELECT Id FROM ClassSubjects); DELETE FROM ClassSubjects; DELETE FROM auth.AspNetUsers WHERE FacultyId<>'' OR Id IN (SELECT ur.UserId FROM auth.AspNetUserRoles ur JOIN auth.AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='BoMon'); COMMIT; PRAGMA foreign_key_check; PRAGMA auth.foreign_key_check; SELECT COUNT(*) AS SubjectsAfter FROM ClassSubjects; SELECT COUNT(*) AS FacultiesAfter FROM auth.AspNetUsers WHERE FacultyId<>''; SELECT COUNT(*) AS RequestsPreserved FROM Requests; DETACH DATABASE auth;")
 $credentials=Join-Path $root 'AppData/khoa-mau.txt'
 if(Test-Path -LiteralPath $credentials) { Move-Item -LiteralPath $credentials -Destination (Join-Path $backup 'khoa-mau.txt') }
 Write-Output "BACKUP=$backup"
}
