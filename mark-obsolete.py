#!/usr/bin/env python3
"""
Script para marcar componentes não utilizados como [Obsolete]
"""

import re
import os

def mark_interface_as_obsolete(file_path, interface_name, message):
    """Marca uma interface como obsolete"""
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Verifica se já está marcado
    if f'[Obsolete("{message}"' in content or '[Obsolete]' in content and interface_name in content[:content.find('interface ' + interface_name) + 100]:
        print(f"✓ {interface_name} já está marcado como [Obsolete]")
        return True
    
    # Padrão para encontrar a interface
    pattern = rf'(/// <\/summary>\s*\n)(public interface {interface_name})'
    replacement = rf'\\1[Obsolete("{message}", false)]\npublic interface {interface_name}'
    
    new_content = re.sub(pattern, replacement, content)
    
    if new_content != content:
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print(f"✓ Marcado {interface_name} como [Obsolete]")
        return True
    else:
        print(f"✗ Falha ao marcar {interface_name}")
        return False


def mark_class_as_obsolete(file_path, class_name, message):
    """Marca uma classe como obsolete"""
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Verifica se já está marcado
    if f'[Obsolete("{message}"' in content or '[Obsolete]' in content and class_name in content[:content.find('class ' + class_name) + 100]:
        print(f"✓ {class_name} já está marcado como [Obsolete]")
        return True
    
    # Padrão para encontrar a classe
    pattern = rf'(/// <\/summary>\s*\n)(public class {class_name})'
    replacement = rf'\\1[Obsolete("{message}", false)]\npublic class {class_name}'
    
    new_content = re.sub(pattern, replacement, content)
    
    if new_content != content:
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print(f"✓ Marcado {class_name} como [Obsolete]")
        return True
    else:
        print(f"✗ Falha ao marcar {class_name}")
        return False


def main():
    repo_path = r"C:\Users\herna\source\repos\DnsBlocker"
    base_path = os.path.join(repo_path, r"src\NextDnsBetBlocker.Core")
    
    print("═" * 60)
    print("  Marcando componentes não utilizados como [Obsolete]")
    print("═" * 60)
    print()
    
    # INTERFACES
    print("Marcando INTERFACES...")
    print()
    
    mark_interface_as_obsolete(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "INextDnsClient",
        "This interface is not used in the current implementation. Use ILogsProducer instead."
    )
    
    mark_interface_as_obsolete(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "ICheckpointStore",
        "This interface is not used in the current implementation."
    )
    
    mark_interface_as_obsolete(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "IBlockedDomainStore",
        "This interface is not used in the current implementation."
    )
    
    mark_interface_as_obsolete(
        os.path.join(base_path, r"Interfaces\Interfaces.cs"),
        "IGamblingSuspectAnalyzer",
        "This interface is not used in the current implementation."
    )
    
    print()
    print("Marcando CLASSES...")
    print()
    
    mark_class_as_obsolete(
        os.path.join(base_path, r"Services\NextDnsClient.cs"),
        "NextDnsClient",
        "This class is not used in the current implementation. Use LogsProducer instead."
    )
    
    mark_class_as_obsolete(
        os.path.join(base_path, r"Services\BlockedDomainStore.cs"),
        "BlockedDomainStore",
        "This class is not used in the current implementation."
    )
    
    mark_class_as_obsolete(
        os.path.join(base_path, r"Services\CheckpointStore.cs"),
        "CheckpointStore",
        "This class is not used in the current implementation."
    )
    
    mark_class_as_obsolete(
        os.path.join(base_path, r"Services\GamblingSuspectAnalyzer.cs"),
        "GamblingSuspectAnalyzer",
        "This class is not used in the current implementation."
    )
    
    print()
    print("═" * 60)
    print("  ✓ Processo concluído!")
    print("═" * 60)


if __name__ == "__main__":
    main()
