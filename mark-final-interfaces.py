#!/usr/bin/env python3
"""
Script para marcar as 3 interfaces restantes como [Obsolete]
"""

import os
import re

def mark_remaining_interfaces():
    """Marca as 3 interfaces restantes"""
    
    file_path = r"C:\Users\herna\source\repos\DnsBlocker\src\NextDnsBetBlocker.Core\Interfaces\Interfaces.cs"
    
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # === ICheckpointStore ===
    content = content.replace(
        'public interface ICheckpointStore\n{',
        '[Obsolete("This interface is not used in the current implementation.", false)]\npublic interface ICheckpointStore\n{'
    )
    
    # === IBlockedDomainStore ===
    content = content.replace(
        'public interface IBlockedDomainStore\n{',
        '[Obsolete("This interface is not used in the current implementation.", false)]\npublic interface IBlockedDomainStore\n{'
    )
    
    # === IGamblingSuspectAnalyzer ===
    content = content.replace(
        'public interface IGamblingSuspectAnalyzer\n{',
        '[Obsolete("This interface is not used in the current implementation.", false)]\npublic interface IGamblingSuspectAnalyzer\n{'
    )
    
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(content)
    
    print("✓ Todas as interfaces marcadas como [Obsolete]")


if __name__ == "__main__":
    mark_remaining_interfaces()
