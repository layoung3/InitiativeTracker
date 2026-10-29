import importlib.machinery, importlib.util, pathlib, unittest, unittest.mock
P=pathlib.Path(__file__).resolve().parents[1] / 'initiativetracker-web-network-bridge'
loader=importlib.machinery.SourceFileLoader('bridge',str(P));spec=importlib.util.spec_from_loader(loader.name,loader);mod=importlib.util.module_from_spec(spec);loader.exec_module(mod)
class BridgeTests(unittest.TestCase):
    @unittest.mock.patch.object(mod.os,'geteuid',return_value=0)
    @unittest.mock.patch.object(mod.subprocess,'run')
    def test_only_saved_uuid(self,run,euid):
        with self.assertRaises(ValueError):mod.execute('begin-return',';rm -rf /')
        run.assert_not_called()
    @unittest.mock.patch.object(mod.os,'geteuid',return_value=0)
    @unittest.mock.patch.object(mod.subprocess,'run')
    def test_detached_transaction(self,run,euid):
        run.return_value.returncode=0;run.return_value.stdout='';mod.execute('begin-return','c7ce4ffc-e066-3d85-8917-90322686badf')
        args=run.call_args.args[0];self.assertEqual(args[0],'/usr/bin/systemd-run');self.assertEqual(args[-4:],['--uuid','c7ce4ffc-e066-3d85-8917-90322686badf','--seconds','180'])
    @unittest.mock.patch.object(mod.os,'geteuid',return_value=0)
    @unittest.mock.patch.object(mod.subprocess,'run')
    def test_confirm_only(self,run,euid):
        run.return_value.returncode=0;run.return_value.stdout='';mod.execute('confirm-return');self.assertEqual(run.call_args.args[0],[mod.RETURN,'confirm'])
    @unittest.mock.patch.object(mod.os,'geteuid',return_value=1000)
    def test_requires_root(self,euid):
        with self.assertRaises(RuntimeError):mod.execute('status')
if __name__=='__main__':unittest.main()
