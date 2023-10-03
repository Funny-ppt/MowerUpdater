需要放置ssh以外的cwRsync在 `bin/rsync/` 下，并创建 `bin/etc/fstab` 文件，内容如下：

`none /cygdrive cygdrive binary,posix=0,user,noacl 0 0`