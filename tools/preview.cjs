const http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../src/AIControl.Host/Web');
http.createServer((req,res)=>{
 const route=new URL(req.url,'http://127.0.0.1').pathname;
 const file=path.resolve(root,'.'+(route==='/'?'/index.html':route));
 if(!file.startsWith(root+path.sep)){res.writeHead(403);return res.end();}
 const type={'.html':'text/html','.js':'text/javascript','.css':'text/css','.svg':'image/svg+xml'}[path.extname(file)]||'application/octet-stream';
 fs.readFile(file,(err,data)=>{res.writeHead(err?404:200,{'Content-Type':type,'Cache-Control':'no-store'});res.end(err?'Not found':data);});
}).listen(17881,'127.0.0.1');
