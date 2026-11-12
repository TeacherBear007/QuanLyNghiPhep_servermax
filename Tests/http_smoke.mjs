// Runs a temporary installation; never opens the project's real AppData.
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, unlink } from 'node:fs/promises';
import { createWriteStream } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import net from 'node:net';

const root=path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const project=path.join(root,'QuanLyNghiPhep');
const directory=await mkdtemp(path.join(tmpdir(),'leave-http-check-'));
const listener=net.createServer();await new Promise(r=>listener.listen(0,'127.0.0.1',r));
const port=listener.address().port;await new Promise(r=>listener.close(r));
const base=`http://127.0.0.1:${port}`;
const log=createWriteStream(path.join(directory,'server.log'));
const processUnderTest=spawn('dotnet',[path.join(project,'bin','Release','net8.0','QuanLyNghiPhep.dll'),'--contentRoot',directory,'--webroot',path.join(project,'wwwroot'),'--urls',base,'--environment','Development'],{windowsHide:true});
processUnderTest.stdout.pipe(log);processUnderTest.stderr.pipe(log);
let passed=0;
const decode=s=>s.replace(/&#x([0-9A-F]+);/gi,(_,n)=>String.fromCodePoint(parseInt(n,16))).replace(/&#(\d+);/g,(_,n)=>String.fromCodePoint(+n)).replace(/&amp;/g,'&').replace(/&quot;/g,'"');
const check=(yes,label)=>{if(!yes)throw Error(label);console.log('PASS '+label);passed++;};
class Client {
    cookies=new Map();
    async request(route,fields) {
        let url=base+route;let method=fields?'POST':'GET';let body=fields?new URLSearchParams(fields):undefined;
        for(let count=0;count<8;count++) {
            const headers={Cookie:[...this.cookies].map(([k,v])=>`${k}=${v}`).join('; ')};
            if(body)headers['Content-Type']='application/x-www-form-urlencoded';
            const response=await fetch(url,{method,body,headers,redirect:'manual'});
            for(const cookie of response.headers.getSetCookie()) {
                const first=cookie.split(';')[0];const eq=first.indexOf('=');this.cookies.set(first.slice(0,eq),first.slice(eq+1));
            }
            if([301,302,303].includes(response.status)){url=new URL(response.headers.get('location'),url).href;method='GET';body=undefined;await response.text();continue;}
            return {status:response.status,text:decode(await response.text()),url};
        }
        throw Error('Redirect loop');
    }
}
const token=text=>{const m=text.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);if(!m)throw Error('Missing CSRF token');return m[1];};
try {
    const anonymous=new Client();let started=false;
    for(let i=0;i<100;i++) {try{const r=await anonymous.request('/');if(r.status===200){started=true;break;}}catch{}await new Promise(r=>setTimeout(r,100));}
    if(!started)throw Error('Server failed to start; see temporary server.log');
    const login=(await anonymous.request('/?username=phongqlhv')).text;
    const loginUsername=login.match(/<input[^>]*id="username"[^>]*>/)?.[0] || '';
    const loginPassword=login.match(/<input[^>]*id="password"[^>]*>/)?.[0] || '';
    check(loginUsername.length>0&&loginPassword.length>0&&!/value="[^"]+"/.test(loginUsername+loginPassword),'Login inputs render empty even with username query parameter');
    check(login.includes('login-brand')&&login.includes("PEOPLE'S SECURITY ACADEMY (PSA)")&&login.includes('password-eye'),'Glass login preserves academy branding and password eye');
    check(loginUsername.includes('autocomplete="off"')&&loginPassword.includes('autocomplete="new-password"'),'Login discourages automatic credential filling');
    check(/js\/app\.js\?v=\d+/.test(login),'Updated login script has cache version');
    check(!(await anonymous.request('/admin')).text.includes('Đổi thông tin tài khoản'),'Anonymous cannot see admin controls');
    check((await anonymous.request('/account/login',{username:'admin',password:'invalid'})).status===400,'Login rejects missing CSRF token');
    const credentials=new Map((await readFile(path.join(directory,'AppData','tai-khoan-ban-dau.txt'),'utf8')).split(/\r?\n/).map(l=>l.split('\t')).filter(x=>x.length===3).map(x=>[x[0],x[2]]));
    const sessions=new Map();
    for(const name of ['admin','trungdoi','phongqlhv','bomon']) {
        const c=new Client();const page=await c.request('/');
        const response=await c.request('/account/login',{username:name,password:credentials.get(name),__RequestVerificationToken:token(page.text)});
        check(response.status===200&&response.text.includes('Đăng xuất'),name+' signs in');sessions.set(name,c);
    }
    check((await sessions.get('admin').request('/admin')).text.includes('Đổi thông tin tài khoản'),'Admin page renders controls');
    check((await sessions.get('trungdoi').request('/')).text.includes('Chọn lớp'),'Manager home renders assigned class picker');
    const teacher=sessions.get('bomon');
    check((await teacher.request('/')).text.includes('TRA CỨU HỌC VIÊN NGHỈ PHÉP'),'Faculty home renders scoped leave lookup');
    check(!(await teacher.request('/admin')).text.includes('Đổi thông tin tài khoản'),'Faculty cannot see admin actions');
    check((await teacher.request('/academic')).status===404,'Removed academic route returns 404');
    check((await sessions.get('phongqlhv').request('/')).text.includes('Xuất Excel'),'Records page provides filtered Excel export');
    const page=await teacher.request('/password');
    check(page.text.includes('Mật khẩu hiện tại'),'Password page renders');
    for(const file of ['accounts.db','business.db','tai-khoan-ban-dau.txt'])check((await teacher.request('/AppData/'+file)).status===404,'Private file blocked: '+file);
    for(const name of ['admin','trungdoi','phongqlhv']) check((await sessions.get(name).request('/password')).text.includes('Mật khẩu hiện tại'),name+' can access own password form');
    const stale=new Client();stale.cookies=new Map(teacher.cookies);
    const changed=await teacher.request('/account/change-password',{current:credentials.get('bomon'),next:'Ab1!xyZ9',confirm:'Ab1!xyZ9',__RequestVerificationToken:token(page.text)});
    check(changed.url.includes('passwordChanged=1'),'Self-service password change succeeds');
    check(!(await stale.request('/')).text.includes('Đăng xuất'),'Old cookie revoked after password change');
    const next=await teacher.request('/');
    const signedIn=await teacher.request('/account/login',{username:'bomon',password:'Ab1!xyZ9',__RequestVerificationToken:token(next.text)});
    check(signedIn.text.includes('Đăng xuất'),'New password signs in');
    console.log(`ALL ${passed} HTTP CHECKS PASSED`);
} finally {
    processUnderTest.kill();
    await new Promise(r=>processUnderTest.exitCode!==null?r():processUnderTest.once('exit',r));
    log.end();
    await unlink(path.join(directory,'AppData','tai-khoan-ban-dau.txt')).catch(()=>{});
}


